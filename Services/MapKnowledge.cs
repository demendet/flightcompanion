using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.World;

namespace FlightCompanion.Services;

/// <summary>
/// Real named lakes, rivers, villages and peaks around the aircraft, from OpenStreetMap. Only the position goes out, rounded by
/// the search radius. If the servers are busy the game carries on with the places it knows by heart.
/// </summary>
public sealed class MapKnowledge
{
    private const int RadiusM = 30000;
    private const double RefreshKm = 18;
    private static readonly string[] Servers = ["https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter", "https://maps.mail.ru/osm/tools/overpass/api/interpreter"];
    private static readonly HttpClient Http = MakeHttp();
    private readonly object _gate = new();
    private readonly Dictionary<string, PointOfInterest> _known = [];
    private double _lastLat = double.NaN, _lastLon = double.NaN;
    private int _busy;

    private static HttpClient MakeHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("FlightCompanion/1.0");
        return h;
    }

    public IReadOnlyList<PointOfInterest> Known { get { lock (_gate) return _known.Values.ToList(); } }

    public void RefreshIfMoved(double lat, double lon, CancellationToken ct)
    {
        if (!double.IsNaN(_lastLat) && Geo.DistanceM(lat, lon, _lastLat, _lastLon) < RefreshKm * 1000) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _lastLat = lat;
        _lastLon = lon;
        _ = Task.Run(async () =>
        {
            try { await FetchAsync(lat, lon, ct); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }, ct);
    }

    /// <summary>Finds a place someone named, on the real map of Finnmark. Null if it does not exist there.</summary>
    public async Task<PointOfInterest?> FindAsync(string name, CancellationToken ct)
    {
        var known = WorldData.Sights.Concat(Known).FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(s.Name.Replace("the ", ""), StringComparison.OrdinalIgnoreCase));
        if (known is not null) return known;
        try
        {
            string url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&bounded=1&viewbox=20.5,71.3,31.5,68.3&q=" + Uri.EscapeDataString(name);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
            var hits = doc.RootElement;
            if (hits.GetArrayLength() == 0) return null;
            var hit = hits[0];
            double lat = double.Parse(hit.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
            double lon = double.Parse(hit.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
            string label = hit.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } s ? s : name;
            return new PointOfInterest(label, lat, lon, "a place the customer asked to see", 10);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            Log.Info("Map", $"Could not look up a place: {ex.GetType().Name}");
            return null;
        }
    }

    public async Task<int> FetchAsync(double lat, double lon, CancellationToken ct)
    {
        string at = string.Create(CultureInfo.InvariantCulture, $"around:{RadiusM},{lat:0.###},{lon:0.###}");
        string query = $"""[out:json][timeout:20];(way["natural"="water"]["name"]({at});relation["natural"="water"]["name"]({at});way["waterway"="river"]["name"]({at});relation["waterway"="river"]["name"]({at});node["place"~"town|village|hamlet"]["name"]({at});node["natural"="peak"]["name"]({at}););out center tags 250;""";
        foreach (var server in Servers)
        {
            try
            {
                using var resp = await Http.PostAsync(server, new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query }), ct);
                if (!resp.IsSuccessStatusCode) continue;
                int n = Parse(await resp.Content.ReadAsStringAsync(ct));
                Log.Info("Map", $"{n} named places around the aircraft from {new Uri(server).Host}");
                return n;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                Log.Info("Map", $"{new Uri(server).Host} did not answer: {ex.GetType().Name}");
            }
        }
        // Try again the next time the aircraft has moved.
        _lastLat = double.NaN;
        return 0;
    }

    private int Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        int count = 0;
        lock (_gate)
        {
            foreach (var e in doc.RootElement.GetProperty("elements").EnumerateArray())
            {
                if (!e.TryGetProperty("tags", out var tags) || !tags.TryGetProperty("name", out var nameEl)) continue;
                string name = tags.TryGetProperty("name:no", out var no) ? no.GetString() ?? "" : nameEl.GetString() ?? "";
                var loc = e.TryGetProperty("center", out var c) ? c : e;
                if (name.Length == 0 || !loc.TryGetProperty("lat", out var la) || !loc.TryGetProperty("lon", out var lo)) continue;
                string Tag(string k) => tags.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
                string place = Tag("place");
                (string note, double range) = Tag("natural") == "water" ? ("a lake", 6.0)
                    : Tag("waterway") == "river" ? ("a river", 7.0)
                    : Tag("natural") == "peak" ? ("a mountain", 8.0)
                    : place == "town" ? ("a town", 14.0)
                    : place == "village" ? ("a village", 10.0)
                    : ("a few houses", 6.0);
                _known[name] = new PointOfInterest(name, la.GetDouble(), lo.GetDouble(), note, range);
                count++;
            }
        }
        return count;
    }
}
