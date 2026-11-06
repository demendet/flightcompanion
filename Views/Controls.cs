using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

/// <summary>
/// The real map of Finnmark. Fits the route by itself until you touch it: the wheel zooms, dragging moves, the buttons in the
/// corner zoom and follow the aircraft. Shows the route with distance and time per leg, how far the fuel reaches, your track so
/// far, runways, and real named places when zoomed in. Dimmed for night use.
/// </summary>
public sealed class TileMap : Control, Avalonia.Rendering.ICustomHitTest
{
    public static readonly StyledProperty<MapData?> DataProperty = AvaloniaProperty.Register<TileMap, MapData?>(nameof(Data));
    public static readonly StyledProperty<PlanePos?> PlaneProperty = AvaloniaProperty.Register<TileMap, PlanePos?>(nameof(Plane));
    public MapData? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public PlanePos? Plane { get => GetValue(PlaneProperty); set => SetValue(PlaneProperty, value); }
    static TileMap() => AffectsRender<TileMap>(DataProperty, PlaneProperty);

    private const int TileSize = 256, MinZoom = 4, MaxZoom = 16;
    private static readonly HttpClient Http = MakeHttp();
    private static readonly ConcurrentDictionary<(int, int, int), Bitmap?> Tiles = new();
    private static readonly Typeface Face = new("Bahnschrift, Inter");
    private static readonly IBrush Water = new SolidColorBrush(Color.Parse("#14262A"));
    private static readonly IBrush Dim = new SolidColorBrush(Color.Parse("#0F1D1C"), 0.42);
    private static readonly IBrush Snow = new SolidColorBrush(Color.Parse("#F4F8F5"));
    private static readonly IBrush Lichen = new SolidColorBrush(Color.Parse("#C4DAD3"));
    private static readonly IBrush Sign = new SolidColorBrush(Color.Parse("#F4C430"));
    private static readonly IBrush Halo = new SolidColorBrush(Color.Parse("#0B1716"), 0.72);
    private static readonly IBrush ButtonBack = new SolidColorBrush(Color.Parse("#1F3A36"), 0.94);
    private static readonly Pen RoutePen = new(new SolidColorBrush(Color.Parse("#F4C430")), 2.4, new DashStyle([5, 3], 0));
    private static readonly Pen NavPen = new(new SolidColorBrush(Color.Parse("#F4F8F5")), 2.2);
    private static readonly Pen TrailPen = new(new SolidColorBrush(Color.Parse("#7FCB8F")), 2);
    private static readonly Pen ReachPen = new(new SolidColorBrush(Color.Parse("#7FCB8F"), 0.8), 1.4, new DashStyle([3, 4], 0));
    private static readonly Pen Outline = new(Brushes.Black, 1);

    private int _zoom = 7;
    private double _cLat = 69.75, _cLon = 25.2;
    private bool _userMoved, _follow;
    private string _fittedFor = "";
    private Point? _dragFrom;
    private double _dragLat, _dragLon;
    private readonly List<Rect> _taken = [];

    private static HttpClient MakeHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("FlightCompanion/1.0");
        return h;
    }

    public bool HitTest(Point point) => true;

    private static Point World(double lat, double lon, int zoom)
    {
        double n = TileSize * Math.Pow(2, zoom);
        double x = (lon + 180) / 360 * n;
        double r = lat * Math.PI / 180;
        double y = (1 - Math.Log(Math.Tan(r) + 1 / Math.Cos(r)) / Math.PI) / 2 * n;
        return new Point(x, y);
    }

    private static (double lat, double lon) Unworld(Point p, int zoom)
    {
        double n = TileSize * Math.Pow(2, zoom);
        double lon = p.X / n * 360 - 180;
        double lat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * p.Y / n))) * 180 / Math.PI;
        return (lat, lon);
    }

    private Rect ZoomInBox => new(Bounds.Width - 38, 8, 30, 30);
    private Rect ZoomOutBox => new(Bounds.Width - 38, 42, 30, 30);
    private Rect FollowBox => new(Bounds.Width - 38, 76, 30, 30);
    private Rect FitBox => new(Bounds.Width - 38, 110, 30, 30);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var at = e.GetPosition(this);
        if (ZoomInBox.Contains(at)) { SetZoom(_zoom + 1); e.Handled = true; return; }
        if (ZoomOutBox.Contains(at)) { SetZoom(_zoom - 1); e.Handled = true; return; }
        if (FollowBox.Contains(at)) { _follow = !_follow; _userMoved = true; InvalidateVisual(); e.Handled = true; return; }
        if (FitBox.Contains(at)) { _follow = false; _userMoved = false; _fittedFor = ""; InvalidateVisual(); e.Handled = true; return; }
        _dragFrom = at;
        _dragLat = _cLat;
        _dragLon = _cLon;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is not { } from) return;
        var now = e.GetPosition(this);
        var c = World(_dragLat, _dragLon, _zoom);
        (_cLat, _cLon) = Unworld(new Point(c.X - (now.X - from.X), c.Y - (now.Y - from.Y)), _zoom);
        _userMoved = true;
        _follow = false;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragFrom = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // The spot under the cursor stays under the cursor.
        var at = e.GetPosition(this);
        var center = World(_cLat, _cLon, _zoom);
        var (lat, lon) = Unworld(new Point(center.X + at.X - Bounds.Width / 2, center.Y + at.Y - Bounds.Height / 2), _zoom);
        SetZoom(_zoom + (e.Delta.Y > 0 ? 1 : -1));
        var spot = World(lat, lon, _zoom);
        (_cLat, _cLon) = Unworld(new Point(spot.X - at.X + Bounds.Width / 2, spot.Y - at.Y + Bounds.Height / 2), _zoom);
        if (_follow) _follow = false;
        e.Handled = true;
    }

    private void SetZoom(int zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _userMoved = true;
        InvalidateVisual();
    }

    /// <summary>Until you touch the map, it shows the whole route and the aircraft.</summary>
    private void FitToRoute(MapData d, double w, double h)
    {
        var lats = new List<double>();
        var lons = new List<double>();
        void Add(double la, double lo) { lats.Add(la); lons.Add(lo); }
        if (d.From is { } a) Add(a.Lat, a.Lon);
        if (d.To is { } b) Add(b.Lat, b.Lon);
        foreach (var s in d.Stops) Add(s.Lat, s.Lon);
        if (Plane is { } p) Add(p.Lat, p.Lon);
        if (lats.Count == 0) { _cLat = 69.75; _cLon = 25.2; _zoom = 7; return; }
        _cLat = (lats.Min() + lats.Max()) / 2;
        _cLon = (lons.Min() + lons.Max()) / 2;
        for (int z = 11; z >= MinZoom; z--)
        {
            var lo = World(lats.Max(), lons.Min(), z);
            var hi = World(lats.Min(), lons.Max(), z);
            if (hi.X - lo.X < w * 0.72 && hi.Y - lo.Y < h * 0.66) { _zoom = z; return; }
        }
        _zoom = MinZoom;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        ctx.FillRectangle(Water, new Rect(0, 0, w, h));
        var d = Data;
        var plane = Plane;
        if (d is not null && !_userMoved)
        {
            string key = $"{d.From?.Id}|{d.To?.Id}|{d.Stops.Count}|{plane is not null}";
            if (key != _fittedFor) { _fittedFor = key; FitToRoute(d, w, h); }
        }
        if (_follow && plane is not null) { _cLat = plane.Lat; _cLon = plane.Lon; }
        if (!_userMoved && d is not null && plane is not null)
        {
            // The aircraft left the picture: show the route and where it has got to again.
            var a = World(plane.Lat, plane.Lon, _zoom);
            var c0 = World(_cLat, _cLon, _zoom);
            if (Math.Abs(a.X - c0.X) > w * 0.46 || Math.Abs(a.Y - c0.Y) > h * 0.46) FitToRoute(d, w, h);
        }

        int zoom = _zoom;
        var center = World(_cLat, _cLon, zoom);
        Point Sc(double lat, double lon)
        {
            var p = World(lat, lon, zoom);
            return new Point(p.X - center.X + w / 2, p.Y - center.Y + h / 2);
        }

        int x0 = (int)Math.Floor((center.X - w / 2) / TileSize), x1 = (int)Math.Floor((center.X + w / 2) / TileSize);
        int y0 = (int)Math.Floor((center.Y - h / 2) / TileSize), y1 = (int)Math.Floor((center.Y + h / 2) / TileSize);
        int max = 1 << zoom;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                if (y < 0 || y >= max || x < 0 || x >= max) continue;
                var dest = new Rect(x * TileSize - center.X + w / 2, y * TileSize - center.Y + h / 2, TileSize, TileSize);
                if (GetTile(zoom, x, y) is { } bmp) { ctx.DrawImage(bmp, dest); continue; }
                // Still on its way: a blown up piece of the tile above it, so zooming never shows empty squares.
                for (int up = 1; up <= 5 && zoom - up >= 0; up++)
                {
                    if (!Tiles.TryGetValue((zoom - up, x >> up, y >> up), out var parent) || parent is null) continue;
                    double size = (double)TileSize / (1 << up);
                    ctx.DrawImage(parent, new Rect((x - ((x >> up) << up)) * size, (y - ((y >> up) << up)) * size, size, size), dest);
                    break;
                }
            }
        ctx.FillRectangle(Dim, new Rect(0, 0, w, h));
        _taken.Clear();

        double metresPerPixel = 40075016.686 * Math.Cos(_cLat * Math.PI / 180) / (TileSize * Math.Pow(2, zoom));

        if (plane is { ReachNm: > 2 } ring)
        {
            double radius = ring.ReachNm * 1852 / metresPerPixel;
            ctx.DrawEllipse(null, ReachPen, Sc(ring.Lat, ring.Lon), radius, radius);
        }
        if (plane?.Trail is { Count: > 1 } trail)
            for (int i = 1; i < trail.Count; i++)
                ctx.DrawLine(TrailPen, Sc(trail[i - 1].Lat, trail[i - 1].Lon), Sc(trail[i].Lat, trail[i].Lon));

        if (d is not null)
        {
            var points = new List<(double Lat, double Lon)>();
            if (d.From is { } from) points.Add((from.Lat, from.Lon));
            foreach (var s in d.Stops) points.Add((s.Lat, s.Lon));
            if (d.To is { } to && (points.Count == 0 || d.Stops.Count > 0 || d.From?.Id != to.Id)) points.Add((to.Lat, to.Lon));
            for (int i = 1; i < points.Count; i++)
            {
                var a = Sc(points[i - 1].Lat, points[i - 1].Lon);
                var b = Sc(points[i].Lat, points[i].Lon);
                ctx.DrawLine(RoutePen, a, b);
                double nm = Geo.DistanceNm(points[i - 1].Lat, points[i - 1].Lon, points[i].Lat, points[i].Lon);
                if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) > 90)
                    Text(ctx, $"{nm:0} nm, {nm / Math.Max(60, d.CruiseKt) * 60:0} min", new Point((a.X + b.X) / 2 + 6, (a.Y + b.Y) / 2 - 8), Sign, 12, true);
            }

            foreach (var p in d.Places)
            {
                var at = Sc(p.Lat, p.Lon);
                if (!OnScreen(at, w, h)) continue;
                bool onRoute = p.Name == d.From?.Name || p.Name == d.To?.Name;
                double s = onRoute ? 5.5 : p.Airport ? 4.5 : 3.5;
                var fill = onRoute ? Sign : p.Airport ? Snow : Lichen;
                if (p.Airport) ctx.DrawRectangle(fill, Outline, new Rect(at.X - s, at.Y - s, s * 2, s * 2));
                else ctx.DrawEllipse(fill, Outline, at, s, s);
                string label = zoom >= 7 ? $"{p.Name}, {p.RunwayM:0} m{(p.HasFuel ? ", " + Game.L.T("fuel") : "")}" : p.Name;
                var size = Measure(label, onRoute ? 13 : 12);
                double x = p.LabelLeft ? at.X - size.Width - s - 3 : at.X + s + 3;
                Text(ctx, label, new Point(x, at.Y - 9), onRoute ? Snow : Lichen, onRoute ? 13 : 12, true);
            }

            if (zoom >= 9)
                foreach (var k in d.Known)
                {
                    var at = Sc(k.Lat, k.Lon);
                    if (!OnScreen(at, w, h)) continue;
                    ctx.DrawEllipse(Lichen, null, at, 1.6, 1.6);
                    Text(ctx, k.Name, new Point(at.X + 4, at.Y - 7), Lichen, 11, false);
                }
            if (zoom >= 8)
                foreach (var k in d.Sights)
                {
                    var at = Sc(k.Lat, k.Lon);
                    if (!OnScreen(at, w, h)) continue;
                    ctx.DrawEllipse(Lichen, Outline, at, 2.6, 2.6);
                    Text(ctx, k.Name, new Point(at.X + 5, at.Y - 7), Snow, 11.5, false);
                }

            foreach (var stop in d.Stops)
            {
                var at = Sc(stop.Lat, stop.Lon);
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(at.X, at.Y - 8), true);
                    c.LineTo(new Point(at.X + 8, at.Y));
                    c.LineTo(new Point(at.X, at.Y + 8));
                    c.LineTo(new Point(at.X - 8, at.Y));
                    c.EndFigure(true);
                }
                ctx.DrawGeometry(stop.Seen ? Lichen : Sign, Outline, g);
                Text(ctx, stop.Name, new Point(at.X + 11, at.Y - 9), stop.Seen ? Lichen : Sign, 13, true);
            }

        }

        if (plane is not null)
        {
            var at = Sc(plane.Lat, plane.Lon);
            if (plane.NavLat is { } nla && plane.NavLon is { } nlo) ctx.DrawLine(NavPen, at, Sc(nla, nlo));
            double r = plane.Heading * Math.PI / 180;
            Point Rot(double dx, double dy) => new(at.X + dx * Math.Cos(r) - dy * Math.Sin(r), at.Y + dx * Math.Sin(r) + dy * Math.Cos(r));
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(Rot(0, -12), true);
                c.LineTo(Rot(2, -3));
                c.LineTo(Rot(11, 3));
                c.LineTo(Rot(2, 2));
                c.LineTo(Rot(1.5, 8));
                c.LineTo(Rot(5, 11));
                c.LineTo(Rot(-5, 11));
                c.LineTo(Rot(-1.5, 8));
                c.LineTo(Rot(-2, 2));
                c.LineTo(Rot(-11, 3));
                c.LineTo(Rot(-2, -3));
                c.EndFigure(true);
            }
            ctx.DrawGeometry(Snow, Outline, g);
        }

        DrawScale(ctx, metresPerPixel, h);
        Button(ctx, ZoomInBox, "+", false);
        Button(ctx, ZoomOutBox, "-", false);
        Button(ctx, FollowBox, "F", _follow);
        Button(ctx, FitBox, "R", !_userMoved);
        var credit = new FormattedText("© OpenStreetMap", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 10, Lichen);
        ctx.FillRectangle(Halo, new Rect(w - credit.Width - 8, h - 16, credit.Width + 8, 16));
        ctx.DrawText(credit, new Point(w - credit.Width - 4, h - 15));
    }

    private static bool OnScreen(Point p, double w, double h) => p.X > -20 && p.Y > -20 && p.X < w + 20 && p.Y < h + 20;

    private static Size Measure(string text, double size)
    {
        var f = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, Snow);
        return new Size(f.Width, f.Height);
    }

    /// <summary>Text with a dark backing so it reads on any part of the map. Labels that would land on another are skipped, unless they matter.</summary>
    private void Text(DrawingContext ctx, string text, Point at, IBrush brush, double size, bool always)
    {
        var f = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush);
        var box = new Rect(at.X - 2, at.Y, f.Width + 4, f.Height);
        if (!always && _taken.Any(t => t.Intersects(box))) return;
        _taken.Add(box);
        ctx.FillRectangle(Halo, box);
        ctx.DrawText(f, at);
    }

    private void Button(DrawingContext ctx, Rect box, string label, bool active)
    {
        ctx.FillRectangle(active ? Sign : ButtonBack, box);
        var f = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 16, active ? Brushes.Black : Snow);
        ctx.DrawText(f, new Point(box.X + (box.Width - f.Width) / 2, box.Y + (box.Height - f.Height) / 2));
    }

    private void DrawScale(DrawingContext ctx, double metresPerPixel, double h)
    {
        double[] options = [1, 2, 5, 10, 20, 50, 100];
        double nm = options.FirstOrDefault(o => o * 1852 / metresPerPixel > 60, 100);
        double px = nm * 1852 / metresPerPixel;
        ctx.FillRectangle(Halo, new Rect(8, h - 26, px + 12, 20));
        ctx.DrawLine(new Pen(Snow, 2), new Point(14, h - 10), new Point(14 + px, h - 10));
        var f = new FormattedText($"{nm:0} nm", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, Snow);
        ctx.DrawText(f, new Point(14, h - 25));
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
