namespace FlightCompanion.Game.Aviation;

public enum StationRole { Pilot, FrontSeat, RearSeat, BaggageA, BaggageB, Other }

public sealed record ProfileStation(StationRole Role, double ArmIn, double MaxLb);

public sealed record EnvelopePoint(double WeightLb, double ArmIn);

/// <summary>
/// Book numbers for one aircraft type. The live sim stays the truth once the load is written (TOTAL WEIGHT, CG PERCENT);
/// these exist so the plan can be judged on the phone, before anything is in the aircraft.
/// </summary>
public sealed record AircraftProfile
{
    public required string Name { get; init; }
    public required string[] TitleMatches { get; init; }
    public required double EmptyWeightLb { get; init; }
    public required double EmptyArmIn { get; init; }
    public required double MaxGrossLb { get; init; }
    public required double FuelArmIn { get; init; }
    public required double UsableFuelGal { get; init; }
    public double FuelLbPerGal { get; init; } = 6.0;
    public required double CruiseKtas { get; init; }
    public required double BurnGph { get; init; }
    public double TaxiFuelGal { get; init; } = 1.4;
    public double MaxCombinedBaggageLb { get; init; } = double.MaxValue;
    public required ProfileStation[] Stations { get; init; }
    /// <summary>Forward CG limit as weight and arm, ascending by weight. Between points the limit is linear.</summary>
    public required EnvelopePoint[] ForwardLimit { get; init; }
    public required double AftLimitIn { get; init; }
    /// <summary>Sea level, 15 C, paved, at MaxGrossLb, over a 50 ft obstacle.</summary>
    public required double TakeoffOver50FtM { get; init; }
    public required double LandingOver50FtM { get; init; }

    public int PassengerSeats => Stations.Count(s => s.Role is StationRole.FrontSeat or StationRole.RearSeat);

    public double ForwardLimitAt(double weightLb)
    {
        if (ForwardLimit.Length == 0) return double.NegativeInfinity;
        if (weightLb <= ForwardLimit[0].WeightLb) return ForwardLimit[0].ArmIn;
        for (int i = 1; i < ForwardLimit.Length; i++)
        {
            var (a, b) = (ForwardLimit[i - 1], ForwardLimit[i]);
            if (weightLb <= b.WeightLb)
                return a.ArmIn + (b.ArmIn - a.ArmIn) * (weightLb - a.WeightLb) / (b.WeightLb - a.WeightLb);
        }
        return ForwardLimit[^1].ArmIn;
    }

    public static readonly AircraftProfile C172 = new()
    {
        Name = "Cessna 172 Skyhawk",
        TitleMatches = ["172"],
        EmptyWeightLb = 1680, EmptyArmIn = 41.0, MaxGrossLb = 2550,
        FuelArmIn = 48.0, UsableFuelGal = 53, FuelLbPerGal = 6.0,
        CruiseKtas = 115, BurnGph = 9.5, MaxCombinedBaggageLb = 120,
        Stations =
        [
            new(StationRole.Pilot, 37, 300),
            new(StationRole.FrontSeat, 37, 300),
            new(StationRole.RearSeat, 73, 300),
            new(StationRole.RearSeat, 73, 300),
            new(StationRole.BaggageA, 95, 120),
            new(StationRole.BaggageB, 123, 50),
        ],
        ForwardLimit = [new(1500, 35.0), new(1950, 35.0), new(2550, 41.0)],
        AftLimitIn = 47.3,
        TakeoffOver50FtM = 497, LandingOver50FtM = 407,
    };

    public static AircraftProfile ForTitle(string? title)
    {
        title ??= "";
        return All.FirstOrDefault(p => p.TitleMatches.Any(m => title.Contains(m, StringComparison.OrdinalIgnoreCase))) ?? C172;
    }

    public static readonly AircraftProfile[] All = [C172];
}
