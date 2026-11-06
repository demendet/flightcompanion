using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FlightCompanion.Game.World;

namespace FlightCompanion.Services;

/// <summary>
/// Real weather: METARs from aviationweather.gov for what the airports report now, and Open-Meteo for what is forecast at a
/// place and date, including strips that have no station. Neither needs a key. Fails soft: no data just means no data.
/// </summary>
public sealed class WeatherFeed
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, WeatherPoint> _metars = [];
    private readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<WeatherPoint> Hours)> _forecasts = [];
    private DateTimeOffset _metarAt = DateTimeOffset.MinValue;

    static WeatherFeed() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("FlightCompanion/1.0");

    public WeatherPoint? Metar(string icao) { lock (_metars) return _metars.GetValueOrDefault(icao); }

    public async Task RefreshMetarsAsync(CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _metarAt < TimeSpan.FromMinutes(10)) return;
        _metarAt = DateTimeOffset.UtcNow;
        try
        {
            string url = "https://aviationweather.gov/api/data/metar?format=json&ids=" + string.Join(",", WorldData.MetarStations);
            var list = WeatherParse.ParseMetars(await Http.GetStringAsync(url, ct));
            lock (_metars) foreach (var (icao, p) in list) _metars[icao] = p;
            Log.Info("Weather", $"{list.Count} METARs");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Error("Weather", "METAR fetch failed", ex);
        }
    }

    /// <summary>Weather for a place at a time: the forecast hour nearest to it.</summary>
    public async Task<WeatherPoint?> ForecastAsync(Place place, DateTimeOffset when, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<WeatherPoint>? hours;
            lock (_forecasts) hours = _forecasts.TryGetValue(place.Id, out var c) && DateTimeOffset.UtcNow - c.At < TimeSpan.FromMinutes(30) ? c.Hours : null;
            if (hours is null)
            {
                string url = string.Create(CultureInfo.InvariantCulture,
                    $"https://api.open-meteo.com/v1/forecast?latitude={place.Lat:0.###}&longitude={place.Lon:0.###}&hourly=temperature_2m,precipitation,snowfall,cloud_cover_low,visibility,wind_speed_10m,wind_gusts_10m,wind_direction_10m&wind_speed_unit=kn&timezone=Europe%2FOslo&forecast_days=10");
                hours = WeatherParse.ParseForecast(await Http.GetStringAsync(url, ct));
                lock (_forecasts) _forecasts[place.Id] = (DateTimeOffset.UtcNow, hours);
            }
            return hours.Count == 0 ? null : hours.OrderBy(h => Math.Abs((h.Time - when).TotalMinutes)).First();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Error("Weather", $"Forecast fetch for {place.Name} failed", ex);
            return null;
        }
    }

    /// <summary>What a tourist's phone shows: a sunnier, vaguer version of the truth, as phone apps tend to be.</summary>
    public static string PhoneAppVersion(WeatherPoint? truth)
    {
        if (truth is null) return "probably fine, partly sunny";
        string sky = truth.CeilingFt is null ? "sunny" : truth.CeilingFt > 1500 ? "partly cloudy" : "mostly cloudy";
        return truth.TempC is { } t ? $"{sky} and {t:0} degrees" : sky;
    }
}
