using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FlightCompanion.Models;

namespace FlightCompanion.Services
{
    public interface IMapService
    {
        Control MapBrowser { get; }
        Task InitializeAsync();
        Task UpdatePlayerPositionAsync(PlayerData player);
        Task RemovePlayerAsync(string playerId);
        Task CenterOnLocationAsync(double lat, double lon, int zoom = 13);
        Task UpdateConnectionStatusAsync(bool connected);
        Task SetRouteAsync(IReadOnlyList<(double Lat, double Lon)> waypoints);
    }

    // Real OpenStreetMap map rendered from tiles (no WebView) with a route line + markers.
    public class MapService : IMapService
    {
        private const int Zoom = 11;
        // Northern-Norway view window (covers the 3 lodges with margin).
        private const double MinLat = 69.30, MaxLat = 69.70, MinLon = 23.90, MaxLon = 25.90;

        private static readonly Dictionary<string, (double Lat, double Lon)> _locations = new()
        {
            { "Mollisjok Lodge", (69.5283, 24.3901) },
            { "Maggie Beach", (69.4562, 25.4585) },
            { "Ravnastua Fjellstue", (69.5269, 24.9556) }
        };

        private readonly IDebugLogService _logger;
        private readonly HttpClient _http = new();
        private readonly Dictionary<string, Control> _playerIcons = new();

        private Border? _mapControl;
        private Canvas? _overlay;
        private Polyline? _routeLine;
        private TextBlock? _connText;
        private bool _isInitialized;

        private int _minTileX, _minTileY;
        private double _worldW, _worldH;

        public Control MapBrowser => _mapControl ?? throw new InvalidOperationException("Map not initialized");

        public MapService(IDebugLogService logger)
        {
            _logger = logger;
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("FlightCompanion/1.0 (personal MSFS tool)");
        }

        // --- Web Mercator (matches OSM tiles) ---
        private static double LonToTileX(double lon) => (lon + 180.0) / 360.0 * (1 << Zoom);
        private static double LatToTileY(double lat)
        {
            var r = lat * Math.PI / 180.0;
            return (1.0 - Math.Log(Math.Tan(r) + 1.0 / Math.Cos(r)) / Math.PI) / 2.0 * (1 << Zoom);
        }
        private (double X, double Y) ToPixel(double lat, double lon) =>
            (LonToTileX(lon) * 256 - _minTileX * 256, LatToTileY(lat) * 256 - _minTileY * 256);

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;
            _logger.LogMap("Initializing OpenStreetMap tiles...");

            _minTileX = (int)Math.Floor(LonToTileX(MinLon));
            var maxTileX = (int)Math.Floor(LonToTileX(MaxLon));
            _minTileY = (int)Math.Floor(LatToTileY(MaxLat)); // north = smaller Y
            var maxTileY = (int)Math.Floor(LatToTileY(MinLat));
            int cols = maxTileX - _minTileX + 1;
            int rows = maxTileY - _minTileY + 1;
            _worldW = cols * 256.0;
            _worldH = rows * 256.0;

            var cacheDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlightCompanion", "MapTiles");
            Directory.CreateDirectory(cacheDir);

            var loaded = new List<(int Col, int Row, Bitmap Bmp)>();
            for (int tx = _minTileX; tx <= maxTileX; tx++)
            {
                for (int ty = _minTileY; ty <= maxTileY; ty++)
                {
                    try
                    {
                        var file = System.IO.Path.Combine(cacheDir, $"{Zoom}_{tx}_{ty}.png");
                        if (!File.Exists(file))
                        {
                            var bytes = await _http.GetByteArrayAsync($"https://tile.openstreetmap.org/{Zoom}/{tx}/{ty}.png");
                            await File.WriteAllBytesAsync(file, bytes);
                        }
                        loaded.Add((tx - _minTileX, ty - _minTileY, new Bitmap(file)));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogMap($"tile {Zoom}/{tx}/{ty} failed: {ex.Message}");
                    }
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() => BuildUi(loaded));
            _isInitialized = true;
            _logger.LogMap($"OpenStreetMap ready ({cols}x{rows} tiles, {loaded.Count} loaded).");
        }

        private void BuildUi(List<(int Col, int Row, Bitmap Bmp)> tiles)
        {
            var tileCanvas = new Canvas { Width = _worldW, Height = _worldH };
            foreach (var (col, row, bmp) in tiles)
            {
                var img = new Image { Source = bmp, Width = 256, Height = 256 };
                Canvas.SetLeft(img, col * 256);
                Canvas.SetTop(img, row * 256);
                tileCanvas.Children.Add(img);
            }

            _overlay = new Canvas { Width = _worldW, Height = _worldH };

            _routeLine = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                StrokeThickness = 3,
                StrokeDashArray = new AvaloniaList<double> { 5, 4 }
            };
            _overlay.Children.Add(_routeLine);

            foreach (var loc in _locations)
            {
                var (x, y) = ToPixel(loc.Value.Lat, loc.Value.Lon);
                var marker = MakeLocationMarker(loc.Key);
                Canvas.SetLeft(marker, x - 8);
                Canvas.SetTop(marker, y - 8);
                _overlay.Children.Add(marker);
            }

            var stack = new Panel { Width = _worldW, Height = _worldH };
            stack.Children.Add(tileCanvas);
            stack.Children.Add(_overlay);

            var viewbox = new Viewbox { Child = stack, Stretch = Stretch.Uniform };

            _connText = new TextBlock
            {
                Text = "● Disconnected",
                Foreground = new SolidColorBrush(Color.FromRgb(232, 17, 35)),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Avalonia.Thickness(10, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            var grid = new Grid();
            grid.Children.Add(viewbox);
            grid.Children.Add(_connText);

            _mapControl = new Border
            {
                Child = grid,
                Background = new SolidColorBrush(Color.FromRgb(18, 20, 24)),
                ClipToBounds = true
            };
        }

        private static Control MakeLocationMarker(string name)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            panel.Children.Add(new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                Stroke = Brushes.White,
                StrokeThickness = 2
            });
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                CornerRadius = new Avalonia.CornerRadius(4),
                Padding = new Avalonia.Thickness(5, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 11 }
            });
            return panel;
        }

        public Task SetRouteAsync(IReadOnlyList<(double Lat, double Lon)> waypoints)
        {
            if (!_isInitialized || _routeLine == null) return Task.CompletedTask;
            Dispatcher.UIThread.Post(() =>
            {
                var pts = new Avalonia.Points();
                foreach (var wp in waypoints)
                {
                    var (x, y) = ToPixel(wp.Lat, wp.Lon);
                    pts.Add(new Avalonia.Point(x, y));
                }
                _routeLine!.Points = pts;
            });
            return Task.CompletedTask;
        }

        public Task UpdatePlayerPositionAsync(PlayerData player)
        {
            if (!_isInitialized || _overlay == null) return Task.CompletedTask;
            Dispatcher.UIThread.Post(() =>
            {
                if (_playerIcons.TryGetValue(player.PlayerId, out var old))
                    _overlay!.Children.Remove(old);

                var lat = player.Aircraft.Latitude;
                var lon = player.Aircraft.Longitude;
                var (x, y) = ToPixel(lat, lon);

                var icon = MakeAircraftIcon(player.PlayerName, player.Aircraft.Heading, player.IsLocal);
                Canvas.SetLeft(icon, x - 10);
                Canvas.SetTop(icon, y - 10);
                _overlay!.Children.Add(icon);
                _playerIcons[player.PlayerId] = icon;
            });
            return Task.CompletedTask;
        }

        private static Control MakeAircraftIcon(string name, double heading, bool isLocal)
        {
            var poly = new Polygon
            {
                Points = new Avalonia.Points
                {
                    new Avalonia.Point(10, 0), new Avalonia.Point(4, 20), new Avalonia.Point(10, 15), new Avalonia.Point(16, 20)
                },
                Fill = isLocal ? new SolidColorBrush(Color.FromRgb(0, 255, 120)) : new SolidColorBrush(Color.FromRgb(0, 188, 242)),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Width = 20,
                Height = 20,
                RenderTransform = new RotateTransform(heading),
                RenderTransformOrigin = Avalonia.RelativePoint.Center
            };
            return poly;
        }

        public Task RemovePlayerAsync(string playerId)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_overlay != null && _playerIcons.TryGetValue(playerId, out var icon))
                {
                    _overlay.Children.Remove(icon);
                    _playerIcons.Remove(playerId);
                }
            });
            return Task.CompletedTask;
        }

        public Task CenterOnLocationAsync(double lat, double lon, int zoom = 13) => Task.CompletedTask;

        public Task UpdateConnectionStatusAsync(bool connected)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_connText == null) return;
                _connText.Text = connected ? "● Connected" : "● Disconnected";
                _connText.Foreground = new SolidColorBrush(connected ? Color.FromRgb(16, 124, 16) : Color.FromRgb(232, 17, 35));
            });
            return Task.CompletedTask;
        }
    }
}
