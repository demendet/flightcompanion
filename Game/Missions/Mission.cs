using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.World;

namespace FlightCompanion.Game.Missions;

public enum MissionPhase
{
    Ringing, Negotiating, Booked, Declined, AtPickup, Boarding, Loaded, Airborne, Landed, Complete, Cancelled, NoShow,
}

public sealed class PlanPassenger
{
    public string? PersonId { get; set; }
    public string Name { get; set; } = "";
    public double Kg { get; set; } = PlanDefaults.PassengerKg;
    /// <summary>True once it stopped being a guess: they told you, or you weighed them.</summary>
    public bool KgKnown { get; set; }
}

public static class PlanDefaults
{
    public const double PassengerKg = 84;
}

public sealed record PlanChange(DateTimeOffset When, string Field, string From, string To);

/// <summary>What the pilot and the customer currently believe. Differs from <see cref="MissionTruth"/> until someone checks.</summary>
public sealed class MissionPlan
{
    public string? PickupId { get; set; }
    public string? DropoffId { get; set; }
    public DateTimeOffset? PickupTime { get; set; }
    public List<PlanPassenger> Passengers { get; set; } = [];
    public double BaggageKg { get; set; }
    public string BaggageNote { get; set; } = "";
    public double CargoKg { get; set; }
    public string CargoNote { get; set; } = "";
    /// <summary>The whole fare. When a per person price was agreed this follows the head count.</summary>
    public double? PriceNok { get; set; }
    /// <summary>Set when the deal is per person, so one more passenger means one more fare.</summary>
    public double? PricePerPersonNok { get; set; }

    public void RecalculatePrice()
    {
        if (PricePerPersonNok is { } each) PriceNok = each * Math.Max(1, Passengers.Count);
    }
    /// <summary>Fuel at takeoff. Null until the pilot decides, then the planner suggests one.</summary>
    public double? FuelGal { get; set; }
    public bool Agreed { get; set; }
    /// <summary>When they want to be fetched again, if this is a drop-off with a return.</summary>
    public DateTimeOffset? ReturnTime { get; set; }
    public List<string> Notes { get; set; } = [];
    public List<PlanChange> Changes { get; set; } = [];
    /// <summary>Set when the bags actually went on a scale in front of everyone.</summary>
    public bool BaggageWeighed { get; set; }
    public double PayloadKg => Passengers.Sum(p => p.Kg) + BaggageKg + CargoKg;
}

public sealed class MissionTruth
{
    public List<PlanPassenger> Passengers { get; set; } = [];
    public double BaggageKg { get; set; }
    public double CargoKg { get; set; }
    /// <summary>What they will claim on the phone.</summary>
    public double ClaimedBaggageKg { get; set; }
    public int ClaimedPassengers { get; set; }
    public double ReturnExtraKg { get; set; }
}

/// <summary>One booked job from the first ring to the final payment.</summary>
public sealed class Mission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string TemplateId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Premise { get; set; } = "";
    public string CallerId { get; set; } = "";
    public List<string> CompanionIds { get; set; } = [];
    public MissionPhase Phase { get; set; } = MissionPhase.Ringing;
    public DateTimeOffset CreatedAt { get; set; }
    public MissionPlan Plan { get; set; } = new();
    public MissionTruth Truth { get; set; } = new();
    public List<string> Twists { get; set; } = [];
    public List<string> SecretsForCaller { get; set; } = [];
    public bool Urgent { get; set; }
    public bool Sightseeing { get; set; }
    public string? ArcId { get; set; }
    public int ArcEpisode { get; set; }
    public string? FollowUpOf { get; set; }
    public string? ReferredByName { get; set; }

    /// <summary>At airports they come to wherever you stopped, so the meeting point is the plane, not a spot on a map.</summary>
    public double? MeetLat { get; set; }
    public double? MeetLon { get; set; }
    public DateTimeOffset? ArrivedAtPickup { get; set; }
    public DateTimeOffset? LoadedAt { get; set; }
    public DateTimeOffset? AirborneAt { get; set; }
    public DateTimeOffset? LandedAt { get; set; }
    public double FuelAtTakeoffGal { get; set; }
    public bool WentOverweight { get; set; }
    public double LoadedOverGrossLb { get; set; }
    public bool ReminderSent { get; set; }
    public bool LateCallMade { get; set; }
    public FlightScore Score { get; set; } = new();
    public FlightReview? Review { get; set; }
    public List<string> Transcript { get; set; } = [];

    public Place? Pickup => WorldData.ById(Plan.PickupId);
    public Place? Dropoff => WorldData.ById(Plan.DropoffId);
    public bool IsOpen => Phase is MissionPhase.Booked or MissionPhase.AtPickup or MissionPhase.Boarding
        or MissionPhase.Loaded or MissionPhase.Airborne or MissionPhase.Landed;
    public bool IsFlying => Phase is MissionPhase.Loaded or MissionPhase.Airborne;
}

/// <summary>Raw facts collected during the flight, scored at the end.</summary>
public sealed class FlightScore
{
    public double ComfortMin { get; set; } = 100;
    public double ComfortAvgSum { get; set; }
    public int ComfortSamples { get; set; }
    public int StallWarnings { get; set; }
    public int SteepTurns { get; set; }
    public int HardMoments { get; set; }
    public double TouchdownFpm { get; set; }
    public double TouchdownG { get; set; }
    public int Bounces { get; set; }
    public double MaxG { get; set; } = 1;
    public double MinG { get; set; } = 1;
    public bool LandedAtDestination { get; set; }
    public double FuelBurnedGal { get; set; }
    public double AirborneHours { get; set; }
    public int BaggageLeftBehindKg { get; set; }
}

public sealed record FlightReview
{
    public required double Stars { get; init; }
    public required double PayNok { get; init; }
    public required double TipNok { get; init; }
    public required double RepairNok { get; init; }
    public required double FuelCostNok { get; init; }
    public required double ReputationDelta { get; init; }
    public required double TrustDelta { get; init; }
    public required double LateMinutes { get; init; }
    public required string LandingVerdict { get; init; }
    public required IReadOnlyList<string> Points { get; init; }
    public string? Quote { get; set; }
}

public static class PlanEvaluator
{
    /// <summary>The full live card calculation. Everything derives from the plan and the book numbers.</summary>
    public static (LoadResult Load, FuelPlan Fuel, double DistanceNm) Evaluate(Mission m, AircraftProfile a, double pilotKg,
        double headwindKt = 0, double oatC = 5, double? fuelOverrideGal = null)
    {
        var from = m.Pickup; var to = m.Dropoff;
        double nm = from is not null && to is not null ? Geo.DistanceNm(from.Lat, from.Lon, to.Lat, to.Lon) : 0;
        bool night = from is not null && m.Plan.PickupTime is { } t && Sun.IsDark(t, from.Lat, from.Lon);
        var fuel = FuelPlanner.Plan(a, nm, headwindKt, night, m.Sightseeing ? 30 : 0);
        double fuelGal = fuelOverrideGal ?? m.Plan.FuelGal ?? Math.Min(a.UsableFuelGal, Math.Ceiling(fuel.RequiredGal));
        var load = LoadPlanner.Compute(new LoadRequest
        {
            Aircraft = a, PilotKg = pilotKg,
            PassengerKg = m.Plan.Passengers.Select(p => p.Kg).ToList(),
            BaggageKg = m.Plan.BaggageKg + m.Plan.CargoKg,
            FuelGal = fuelGal, TripFuelGal = fuel.TripGal + fuel.TaxiGal,
            OatC = oatC, Departure = from?.Runway, Arrival = to?.Runway,
        });
        var extra = new List<Finding>(load.Findings);
        if (fuelGal < fuel.RequiredGal - 0.05)
            extra.Add(new(fuelGal < fuel.TripGal + fuel.TaxiGal ? FindingLevel.Stop : FindingLevel.Caution, "fuel",
                L.F("Fuel {0:0} gal, needs {1:0} gal with reserve", fuelGal, fuel.RequiredGal)));
        if (fuel.RequiredGal > a.UsableFuelGal)
            extra.Add(new(FindingLevel.Stop, "range", L.F("Trip needs {0:0} gal, tanks hold {1:0}", fuel.RequiredGal, a.UsableFuelGal)));
        return (load with { Findings = extra }, fuel, nm);
    }
}
