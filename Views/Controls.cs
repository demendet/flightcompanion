using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FlightCompanion.Game.Aviation;
using FlightCompanion.Services;
using FlightCompanion.ViewModels;

namespace FlightCompanion.Views;

/// <summary>Translated literal for XAML. The window is built after the language is known.</summary>
public sealed class TrExtension : Avalonia.Markup.Xaml.MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string text) => Text = text;
    public string Text { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => Game.L.T(Text);
}

/// <summary>
/// The load sheet's envelope, the same picture as in the aircraft's book. It is the centre of the screen on purpose: when a
/// tourist on the phone adds a suitcase, this point moves while they are still talking.
/// </summary>
public sealed class EnvelopeChart : Control
{
    public static readonly StyledProperty<EnvelopeData?> DataProperty = AvaloniaProperty.Register<EnvelopeChart, EnvelopeData?>(nameof(Data));
    public EnvelopeData? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    static EnvelopeChart() => AffectsRender<EnvelopeChart>(DataProperty);

    private static readonly IBrush Fill = new SolidColorBrush(Color.Parse("#1F3F3A"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#7F9C95"));
    private static readonly IBrush Snow = new SolidColorBrush(Color.Parse("#E6EEE9"));
    private static readonly Pen Grid = new(new SolidColorBrush(Color.Parse("#223734")), 1);
    private static readonly Pen Edge = new(new SolidColorBrush(Color.Parse("#7FCB8F")), 1.6);
    private static readonly Pen Burn = new(new SolidColorBrush(Color.Parse("#E6EEE9")), 1.4, new DashStyle([3, 3], 0));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#EE6A5E"));
    private static readonly IBrush Sign = new SolidColorBrush(Color.Parse("#F4C430"));
    private static readonly Typeface Face = new("Bahnschrift, Inter");

    public override void Render(DrawingContext ctx)
    {
        var d = Data;
        double w = Bounds.Width, h = Bounds.Height;
        const double left = 46, right = 12, top = 10, bottom = 40;
        double pw = w - left - right, ph = h - top - bottom;
        if (d is null || pw < 40 || ph < 40) return;
        var a = d.Profile;

        double minArm = a.ForwardLimit[0].ArmIn - 2.5, maxArm = a.AftLimitIn + 2.5;
        double minLb = a.ForwardLimit[0].WeightLb - 80, maxLb = a.MaxGrossLb * 1.1;
        Point P(double arm, double lb) => new(left + (Math.Clamp(arm, minArm, maxArm) - minArm) / (maxArm - minArm) * pw,
            top + ph - (Math.Clamp(lb, minLb, maxLb) - minLb) / (maxLb - minLb) * ph);

        // Weight lines in kilos, since everyone here talks in kilos.
        for (double kg = 700; kg <= 1300; kg += 100)
        {
            double lb = Units.KgToLb(kg);
            if (lb < minLb || lb > maxLb) continue;
            var y = P(minArm, lb).Y;
            ctx.DrawLine(Grid, new Point(left, y), new Point(w - right, y));
            Label(ctx, $"{kg:0}", new Point(left - 6, y - 7), Ink, TextAlignment.Right);
        }
        for (double arm = Math.Ceiling(minArm / 5) * 5; arm <= maxArm; arm += 5)
        {
            var x = P(arm, minLb).X;
            ctx.DrawLine(Grid, new Point(x, top), new Point(x, top + ph));
            Label(ctx, $"{arm:0}", new Point(x, top + ph + 4), Ink, TextAlignment.Center);
        }
        Label(ctx, "kg", new Point(left - 6, top + ph + 4), Ink, TextAlignment.Right);
        Label(ctx, Game.L.Norwegian ? "tommer bak referansepunktet" : "inches behind the datum", new Point(left + pw / 2, top + ph + 22), Ink, TextAlignment.Center, 11);

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(P(a.ForwardLimit[0].ArmIn, a.ForwardLimit[0].WeightLb), true);
            for (int i = 1; i < a.ForwardLimit.Length; i++) g.LineTo(P(a.ForwardLimit[i].ArmIn, a.ForwardLimit[i].WeightLb));
            g.LineTo(P(a.AftLimitIn, a.MaxGrossLb));
            g.LineTo(P(a.AftLimitIn, a.ForwardLimit[0].WeightLb));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(Fill, Edge, geo);

        var to = P(d.TakeoffCg, d.TakeoffLb);
        var ld = P(d.LandingCg, d.LandingLb);
        // Short trips burn so little fuel that takeoff and landing are the same spot: then one mark says it all.
        bool apart = Math.Abs(to.X - ld.X) + Math.Abs(to.Y - ld.Y) > 18;
        if (apart)
        {
            ctx.DrawLine(Burn, to, ld);
            ctx.DrawEllipse(null, new Pen(Snow, 1.5), ld, 4, 4);
            Label(ctx, Game.L.Norwegian ? "landing" : "landing", new Point(ld.X + 8, ld.Y - 7), Ink, TextAlignment.Left, 11);
        }
        var mark = d.Legal ? Sign : Warn;
        ctx.DrawEllipse(mark, new Pen(Brushes.Black, 1), to, 7, 7);
        Label(ctx, Game.L.Norwegian ? "avgang" : "takeoff", new Point(to.X + 11, to.Y - 8), Snow, TextAlignment.Left, 12);
    }

    private static void Label(DrawingContext ctx, string text, Point at, IBrush brush, TextAlignment align, double size = 11)
    {
        var f = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush);
        double x = align switch { TextAlignment.Right => at.X - f.Width, TextAlignment.Center => at.X - f.Width / 2, _ => at.X };
        ctx.DrawText(f, new Point(x, at.Y));
    }
}

/// <summary>A real map of Finnmark, from cached OpenStreetMap tiles, dimmed for night use.</summary>
public sealed class TileMap : Control
{
    public static readonly StyledProperty<MapData?> DataProperty = AvaloniaProperty.Register<TileMap, MapData?>(nameof(Data));
    public MapData? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    static TileMap() => AffectsRender<TileMap>(DataProperty);

    private const int TileSize = 256;
    private const double CenterLat = 69.75, CenterLon = 25.2;
    private static readonly HttpClient Http = MakeHttp();
    private static readonly ConcurrentDictionary<(int, int, int), Bitmap?> Tiles = new();
    private static readonly Typeface Face = new("Bahnschrift, Inter");
    private static readonly IBrush Water = new SolidColorBrush(Color.Parse("#14262A"));
    private static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#0F1D1C"), 0.62);
    private static readonly IBrush Snow = new SolidColorBrush(Color.Parse("#E6EEE9"));
    private static readonly IBrush Lichen = new SolidColorBrush(Color.Parse("#A9C4BC"));
    private static readonly IBrush Sign = new SolidColorBrush(Color.Parse("#F4C430"));
    private static readonly Pen Route = new(new SolidColorBrush(Color.Parse("#F4C430")), 2, new DashStyle([4, 3], 0));

    private static HttpClient MakeHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("FlightCompanion/1.0");
        return h;
    }

    private static Point World(double lat, double lon, int zoom)
    {
        double n = TileSize * Math.Pow(2, zoom);
        double x = (lon + 180) / 360 * n;
        double r = lat * Math.PI / 180;
        double y = (1 - Math.Log(Math.Tan(r) + 1 / Math.Cos(r)) / Math.PI) / 2 * n;
        return new Point(x, y);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        ctx.FillRectangle(Water, new Rect(0, 0, w, h));
        var d = Data;
        double cLat = CenterLat, cLon = CenterLon;
        int zoom = 7;
        if (d?.From is { } a && d.To is { } b)
        {
            cLat = (a.Lat + b.Lat) / 2;
            cLon = (a.Lon + b.Lon) / 2;
            double nm = Geo.DistanceNm(a.Lat, a.Lon, b.Lat, b.Lon);
            zoom = nm < 30 ? 8 : nm < 95 ? 7 : 6;
        }
        var center = World(cLat, cLon, zoom);
        Point W(double lat, double lon) => World(lat, lon, zoom);
        Point S(Point p) => new(p.X - center.X + w / 2, p.Y - center.Y + h / 2);

        int x0 = (int)Math.Floor((center.X - w / 2) / TileSize), x1 = (int)Math.Floor((center.X + w / 2) / TileSize);
        int y0 = (int)Math.Floor((center.Y - h / 2) / TileSize), y1 = (int)Math.Floor((center.Y + h / 2) / TileSize);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (GetTile(zoom, x, y) is { } bmp)
                {
                    var at = S(new Point(x * TileSize, y * TileSize));
                    ctx.DrawImage(bmp, new Rect(at.X, at.Y, TileSize, TileSize));
                }
        ctx.FillRectangle(Dim, new Rect(0, 0, w, h));

        if (d is null) return;
        if (d.From is { } from && d.To is { } to && from.Id != to.Id)
            ctx.DrawLine(Route, S(W(from.Lat, from.Lon)), S(W(to.Lat, to.Lon)));
        foreach (var p in d.Places)
        {
            var at = S(W(p.Lat, p.Lon));
            bool onRoute = p.Name == d.From?.Name || p.Name == d.To?.Name;
            ctx.DrawEllipse(onRoute ? Sign : Lichen, null, at, onRoute ? 4.5 : 3, onRoute ? 4.5 : 3);
            var t = new FormattedText(p.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, onRoute ? 13 : 11.5, onRoute ? Snow : Lichen);
            ctx.DrawText(t, new Point(p.LabelLeft ? at.X - t.Width - 7 : at.X + 7, at.Y - 8));
        }
        if (d.PlaneLat is { } lat && d.PlaneLon is { } lon)
        {
            var at = S(W(lat, lon));
            double r = d.PlaneHeading * Math.PI / 180;
            Point Rot(double dx, double dy) => new(at.X + dx * Math.Cos(r) - dy * Math.Sin(r), at.Y + dx * Math.Sin(r) + dy * Math.Cos(r));
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(Rot(0, -9), true);
                c.LineTo(Rot(6, 7));
                c.LineTo(Rot(0, 3));
                c.LineTo(Rot(-6, 7));
                c.EndFigure(true);
            }
            ctx.DrawGeometry(Snow, new Pen(Brushes.Black, 1), g);
        }
    }

    private Bitmap? GetTile(int zoom, int x, int y)
    {
        if (Tiles.TryGetValue((zoom, x, y), out var cached)) return cached;
        Tiles[(zoom, x, y)] = null;
        string path = Path.Combine(AppPaths.MapTiles, $"{zoom}_{x}_{y}.png");
        _ = Task.Run(async () =>
        {
            try
            {
                if (!File.Exists(path))
                    await File.WriteAllBytesAsync(path, await Http.GetByteArrayAsync($"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png"));
                Tiles[(zoom, x, y)] = new Bitmap(path);
                Dispatcher.UIThread.Post(InvalidateVisual);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                Log.Error("Map", $"Tile {x},{y} failed", ex);
            }
        });
        return null;
    }
}

/// <summary>A plain bar with a mark on it: the fuel the trip needs against what is in the tanks, or the load against the limit.</summary>
public sealed class MarkBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<MarkBar, double>(nameof(Value));
    public static readonly StyledProperty<double> MarkProperty = AvaloniaProperty.Register<MarkBar, double>(nameof(Mark), -1);
    public static readonly StyledProperty<IBrush?> BarProperty = AvaloniaProperty.Register<MarkBar, IBrush?>(nameof(Bar));
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Mark { get => GetValue(MarkProperty); set => SetValue(MarkProperty, value); }
    public IBrush? Bar { get => GetValue(BarProperty); set => SetValue(BarProperty, value); }
    static MarkBar() => AffectsRender<MarkBar>(ValueProperty, MarkProperty, BarProperty);

    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#223734"));
    private static readonly Pen MarkPen = new(new SolidColorBrush(Color.Parse("#E6EEE9")), 2);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        ctx.FillRectangle(Track, new Rect(0, 0, w, h));
        ctx.FillRectangle(Bar ?? Brushes.Gray, new Rect(0, 0, w * Math.Clamp(Value, 0, 1), h));
        if (Mark >= 0) ctx.DrawLine(MarkPen, new Point(w * Mark, -2), new Point(w * Mark, h + 2));
    }
}
