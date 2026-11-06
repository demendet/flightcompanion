using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.World;

namespace FlightCompanion.Game.Missions;

/// <summary>
/// Answers the only fuel question that matters: how much do I need for this job from where I am now.
/// One way covers getting to the pickup, the trip and the reserve. Round trip adds the way home for a place with no pump.
/// </summary>
public sealed record FuelAdvice
{
    public required double OneWayGal { get; init; }
    public required double RoundTripGal { get; init; }
    public required double MinutesOneWay { get; init; }
    public required bool FuelAtDestination { get; init; }
    public required bool RoundTripFits { get; init; }
    public required bool OneWayFits { get; init; }
}

public static class FuelAdvisor
{
    private const double SameSpotNm = 1.5;

    public static FuelAdvice? Advise(AircraftProfile a, double? lat, double? lon, Mission? m, double cachedAtDestinationGal, double headwindKt = 0)
    {
        if (m?.Pickup is not { } from || m.Dropoff is not { } to) return null;
        bool flying = m.Phase is MissionPhase.Airborne;
        double toPickup = !flying && m.Phase is MissionPhase.Booked or MissionPhase.Negotiating or MissionPhase.Ringing && lat is { } la && lon is { } lo
            ? Math.Max(0, Geo.DistanceNm(la, lo, from.Lat, from.Lon)) : 0;
        if (toPickup < SameSpotNm) toPickup = 0;
        double trip = m.RouteNm();
        bool night = m.Plan.PickupTime is { } t && Sun.IsDark(t, from.Lat, from.Lon);
        double extra = m.Sightseeing ? 30 : 0;

        // Each landing and new takeoff is its own bit of climbing, taxiing and circuit, which is why these are separate legs.
        var pickup = FuelPlanner.Plan(a, toPickup, 0, night);
        var main = FuelPlanner.Plan(a, trip, headwindKt, night, extra);
        var back = FuelPlanner.Plan(a, Geo.DistanceNm(from.Lat, from.Lon, to.Lat, to.Lon), -headwindKt, night);

        double positioning = toPickup > 0 ? pickup.TripGal + pickup.TaxiGal : 0;
        double oneWay = positioning + main.TripGal + main.TaxiGal + main.ReserveGal;
        double round = oneWay + (trip > 0 ? back.TripGal + back.TaxiGal : 0);

        return new FuelAdvice
        {
            OneWayGal = Math.Ceiling(oneWay),
            RoundTripGal = Math.Ceiling(round),
            MinutesOneWay = main.TripHours * 60,
            FuelAtDestination = to.FuelPriceNokPerGal is not null || cachedAtDestinationGal >= back.RequiredGal,
            OneWayFits = oneWay <= a.UsableFuelGal,
            RoundTripFits = round <= a.UsableFuelGal,
        };
    }
}
