using System.Text.Json;
using FlightCompanion.Game.Aviation;

namespace FlightCompanion.Game.World;

public enum FlightCategory { Vfr, Marginal, Below, Unknown }

/// <summary>One moment of weather at one place, either observed (METAR) or forecast.</summary>
public sealed record WeatherPoint
{
    public required DateTimeOffset Time { get; init; }
    public string Source { get; init; } = "";
    public double? WindFromDeg { get; init; }
    public double? WindKt { get; init; }
    public double? GustKt { get; init; }
    public double? VisibilityKm { get; init; }
    public double? CeilingFt { get; init; }
    public double? TempC { get; init; }
    public double? PrecipMm { get; init; }
    public double? SnowfallCm { get; init; }
    public string Raw { get; init; } = "";

    public FlightCategory Category
    {
        get
        {
            if (VisibilityKm is null && CeilingFt is null) return FlightCategory.Unknown;
            double vis = VisibilityKm ?? 99, ceil = CeilingFt ?? 99999;
            if (vis < 3 || ceil < 800) return FlightCategory.Below;
            if (vis < 8 || ceil < 1500) return FlightCategory.Marginal;
            return FlightCategory.Vfr;
        }
    }

    /// <summary>The honest one-liner the pilot reads on the card and the AI is told.</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (WindKt is { } w) parts.Add(WindFromDeg is { } d ? L.F("wind {0:000} at {1:0} kt", d, w) : L.F("wind {0:0} kt", w));
        if (GustKt is { } g && g > (WindKt ?? 0) + 4) parts.Add(L.F("gusting {0:0}", g));
        if (VisibilityKm is { } v) parts.Add(v >= 10 ? L.T("visibility 10 km or more") : L.F("visibility {0:0.#} km", v));
        if (CeilingFt is { } c) parts.Add(L.F("ceiling {0:0} ft", c));
        else if (Source == "metar") parts.Add(L.T("no low ceiling"));
        if (TempC is { } t) parts.Add(L.F("{0:0} C", t));
        if (SnowfallCm is > 0.05) parts.Add(L.T("snow"));
        else if (PrecipMm is > 0.1) parts.Add(L.T("rain"));
        return string.Join(", ", parts);
    }
}

public static class WeatherParse
{
    /// <summary>aviationweather.gov /api/data/metar?format=json</summary>
    public static IReadOnlyList<(string Icao, WeatherPoint Point)> ParseMetars(string json)
    {
        var list = new List<(string, WeatherPoint)>();
        using var doc = JsonDocument.Parse(json);
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            string icao = Str(m, "icaoId") ?? "";
            if (icao.Length == 0) continue;
            double? ceiling = null;
            if (m.TryGetProperty("clouds", out var clouds) && clouds.ValueKind == JsonValueKind.Array)
                foreach (var c in clouds.EnumerateArray())
                    if (Str(c, "cover") is "BKN" or "OVC" or "OVX" && Num(c, "base") is { } b)
                        ceiling = ceiling is null ? b : Math.Min(ceiling.Value, b);
            DateTimeOffset when = m.TryGetProperty("obsTime", out var o) && o.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(o.GetInt64()) : DateTimeOffset.UtcNow;
            list.Add((icao, new WeatherPoint
            {
                Time = when, Source = "metar",
                WindFromDeg = Num(m, "wdir"), WindKt = Num(m, "wspd"), GustKt = Num(m, "wgst"),
                VisibilityKm = VisibilityKm(m), CeilingFt = ceiling, TempC = Num(m, "temp"),
                Raw = Str(m, "rawOb") ?? "",
            }));
        }
        return list;
    }

    private static double? VisibilityKm(JsonElement m)
    {
        if (!m.TryGetProperty("visib", out var v)) return null;
        double? sm = v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String => double.TryParse((v.GetString() ?? "").TrimEnd('+'), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null,
        };
        return sm * 1.609;
    }

    /// <summary>api.open-meteo.com hourly forecast for a point.</summary>
    public static IReadOnlyList<WeatherPoint> ParseForecast(string json)
    {
        var result = new List<WeatherPoint>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("hourly", out var h)) return result;
        var times = h.GetProperty("time").EnumerateArray().Select(t => t.GetString() ?? "").ToArray();
        double offsetS = doc.RootElement.TryGetProperty("utc_offset_seconds", out var u) ? u.GetDouble() : 0;
        double?[] Col(string k) => h.TryGetProperty(k, out var a)
            ? a.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetDouble() : (double?)null).ToArray()
            : new double?[times.Length];
        var dir = Col("wind_direction_10m"); var spd = Col("wind_speed_10m"); var gust = Col("wind_gusts_10m");
        var vis = Col("visibility"); var low = Col("cloud_cover_low"); var temp = Col("temperature_2m");
        var rain = Col("precipitation"); var snow = Col("snowfall");
        for (int i = 0; i < times.Length; i++)
        {
            if (!DateTime.TryParse(times[i], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var local)) continue;
            result.Add(new WeatherPoint
            {
                Time = new DateTimeOffset(local, TimeSpan.FromSeconds(offsetS)), Source = "forecast",
                WindFromDeg = dir[i], WindKt = spd[i], GustKt = gust[i],
                VisibilityKm = vis[i] / 1000.0,
                CeilingFt = low[i] >= 60 ? 900 : low[i] >= 30 ? 2500 : null,   // the feed gives cover, not base: low cover at 60 percent reads as a real ceiling
                TempC = temp[i], PrecipMm = rain[i], SnowfallCm = snow[i],
            });
        }
        return result;
    }

    private static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string k)
    {
        if (!e.TryGetProperty(k, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        return null;   // "wdir":"VRB" and friends
    }
}

public sealed record WeatherVerdict(FlightCategory Category, bool Dark, string Text)
{
    public bool Flyable => Category is FlightCategory.Vfr or FlightCategory.Marginal or FlightCategory.Unknown;
}

public static class WeatherAdvice
{
    /// <summary>What a bush pilot would conclude for a takeoff or landing at this place and time.</summary>
    public static WeatherVerdict Judge(WeatherPoint? p, Place place, DateTimeOffset when)
    {
        bool dark = Sun.IsDark(when, place.Lat, place.Lon);
        if (p is null) return new(FlightCategory.Unknown, dark, L.T(dark ? "No weather data, and it is dark" : "No weather data"));
        string text = p.Summary();
        if (dark) text += L.T(", dark");
        return new(p.Category, dark, text);
    }
}
