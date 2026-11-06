using FlightCompanion.Game.Aviation;

namespace FlightCompanion.Game.World;

public sealed record SeenSight(PointOfInterest Sight, double DistanceKm, string Side)
{
    public string Describe() => $"{Sight.Name} {Side}, about {Math.Max(1, Math.Round(DistanceKm))} km away ({Sight.Note})";
}

/// <summary>
/// Knows what can actually be seen from the aircraft right now, and on which side. Passengers only talk about what is really
/// there, so "look at the lake on the left" means there is a lake on the left.
/// </summary>
public sealed class SightWatcher
{
    private readonly HashSet<string> _spoken = [];

    public static string SideOf(double heading, double bearing)
    {
        double rel = (bearing - heading + 360) % 360;
        if (rel < 25 || rel > 335) return "straight ahead";
        if (rel < 155) return "on the right";
        if (rel < 205) return "behind you";
        return "on the left";
    }

    /// <summary>Nearest first, leaving out what is already behind the aircraft.</summary>
    public static IReadOnlyList<SeenSight> Visible(double lat, double lon, double heading, IEnumerable<PointOfInterest> sights, int take = 4)
    {
        var seen = new List<SeenSight>();
        foreach (var s in sights)
        {
            double km = Geo.DistanceM(lat, lon, s.Lat, s.Lon) / 1000;
            if (km > s.RangeKm) continue;
            string side = SideOf(heading, Geo.BearingDeg(lat, lon, s.Lat, s.Lon));
            if (side != "behind you" || km < 1.5) seen.Add(new SeenSight(s, km, km < 1 ? "right below you" : side));
        }
        return seen.OrderBy(s => s.DistanceKm).Take(take).ToList();
    }

    /// <summary>The nearest thing not yet talked about on this flight. Each sight gets one comment, so it never turns into a guided tour on repeat.</summary>
    public SeenSight? NextToMention(double lat, double lon, double heading, IEnumerable<PointOfInterest> sights)
    {
        var next = Visible(lat, lon, heading, sights, 8).FirstOrDefault(s => !_spoken.Contains(s.Sight.Name));
        if (next is not null) _spoken.Add(next.Sight.Name);
        return next;
    }

    public void NewFlight() => _spoken.Clear();
}
