using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.Flight;

namespace FlightCompanion.Game.Missions;

public sealed record NavTarget(string Name, double Lat, double Lon, double CourseMagnetic, double DistanceNm, double Minutes, string Why);

/// <summary>Where to fly right now for this job, from where the aircraft actually is.</summary>
public static class Navigation
{
    public static NavTarget? Next(Mission? m, Telemetry? t, AircraftProfile a)
    {
        if (m is null || t is null) return null;
        (string name, double lat, double lon, string why)? goal = m.Phase switch
        {
            MissionPhase.Booked or MissionPhase.Negotiating when m.Pickup is { } p => (p.Name, p.MeetingLat, p.MeetingLon, "pickup"),
            MissionPhase.Loaded or MissionPhase.Airborne => NextInAir(m),
            _ => null,
        };
        if (goal is not { } g) return null;
        double nm = Geo.DistanceNm(t.Lat, t.Lon, g.lat, g.lon);
        double speed = t.OnGround || t.GroundSpeedKt < 50 ? a.CruiseKtas : t.GroundSpeedKt;
        double course = (Geo.BearingDeg(t.Lat, t.Lon, g.lat, g.lon) - t.MagVarDeg + 360) % 360;
        return new NavTarget(g.name, g.lat, g.lon, course, nm, nm / speed * 60, g.why);
    }

    private static (string, double, double, string)? NextInAir(Mission m)
    {
        var stop = m.Stops.FirstOrDefault(s => !s.Seen);
        if (stop is not null) return (stop.Name, stop.Lat, stop.Lon, "sight");
        return m.Dropoff is { } d ? (d.Name, d.Lat, d.Lon, "dropoff") : null;
    }
}
