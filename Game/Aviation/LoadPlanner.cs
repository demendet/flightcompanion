using static FlightCompanion.Game.L;

namespace FlightCompanion.Game.Aviation;

public enum FindingLevel { Info, Caution, Stop }

public sealed record Finding(FindingLevel Level, string Code, string Text);

public sealed record StationLoad(int ProfileIndex, StationRole Role, string Label, double Lb, double ArmIn);

/// <summary>Everything the card shows about a load. All numbers come from here, never from the AI.</summary>
public sealed record LoadResult
{
    public required IReadOnlyList<StationLoad> Stations { get; init; }
    public required double ZeroFuelLb { get; init; }
    public required double FuelGal { get; init; }
    public required double TakeoffLb { get; init; }
    public required double TakeoffCgIn { get; init; }
    public required double LandingLb { get; init; }
    public required double LandingCgIn { get; init; }
    public required double MaxGrossLb { get; init; }
    public required double ForwardLimitIn { get; init; }
    public required double AftLimitIn { get; init; }
    public required double OverGrossLb { get; init; }
    public required double OverflowBaggageLb { get; init; }
    public required double ExtraPayloadRoomLb { get; init; }
    public required double TakeoffDistanceM { get; init; }
    public required double LandingDistanceM { get; init; }
    public required IReadOnlyList<Finding> Findings { get; init; }
    public bool IsLegal => Findings.All(f => f.Level != FindingLevel.Stop);
}

public sealed record RunwayInfo(double LengthM, double ElevationFt, bool Soft);

public sealed record LoadRequest
{
    public required AircraftProfile Aircraft { get; init; }
    public double PilotKg { get; init; } = 85;
    public required IReadOnlyList<double> PassengerKg { get; init; }
    public double BaggageKg { get; init; }
    public double FuelGal { get; init; }
    public double TripFuelGal { get; init; }
    public double OatC { get; init; } = 15;
    public RunwayInfo? Departure { get; init; }
    public RunwayInfo? Arrival { get; init; }
}

public static class LoadPlanner
{
    public static LoadResult Compute(LoadRequest r)
    {
        var a = r.Aircraft;
        var findings = new List<Finding>();
        var stations = new List<StationLoad>();
        var stationLb = new double[a.Stations.Length];

        int pilotIdx = Array.FindIndex(a.Stations, s => s.Role == StationRole.Pilot);
        if (pilotIdx >= 0) stationLb[pilotIdx] = Units.KgToLb(r.PilotKg);

        // Heaviest passenger up front: that is what a real pilot does to keep the CG forward.
        var seats = a.Stations.Select((s, i) => (s, i))
            .Where(x => x.s.Role is StationRole.FrontSeat or StationRole.RearSeat)
            .OrderBy(x => x.s.ArmIn).Select(x => x.i).ToArray();
        var pax = r.PassengerKg.OrderByDescending(k => k).ToArray();
        for (int i = 0; i < pax.Length && i < seats.Length; i++) stationLb[seats[i]] = Units.KgToLb(pax[i]);
        if (pax.Length > seats.Length)
            findings.Add(new(FindingLevel.Stop, "seats", F("{0} passengers but only {1} seats", pax.Length, seats.Length)));

        // Baggage fills the forward area first, then the rear one, within both placard limits.
        double bagLeft = Units.KgToLb(r.BaggageKg);
        double combinedLeft = a.MaxCombinedBaggageLb;
        foreach (var (s, i) in a.Stations.Select((s, i) => (s, i))
                     .Where(x => x.s.Role is StationRole.BaggageA or StationRole.BaggageB).OrderBy(x => x.s.ArmIn))
        {
            double put = Math.Min(Math.Min(bagLeft, s.MaxLb), combinedLeft);
            stationLb[i] += put;
            bagLeft -= put;
            combinedLeft -= put;
        }
        double overflow = bagLeft;
        if (overflow > 0.5)
            findings.Add(new(FindingLevel.Stop, "baggage", F("{0:0} kg of baggage has no legal place to go", Units.LbToKg(overflow))));

        double weight = a.EmptyWeightLb, moment = a.EmptyWeightLb * a.EmptyArmIn;
        for (int i = 0; i < a.Stations.Length; i++)
        {
            var s = a.Stations[i];
            weight += stationLb[i];
            moment += stationLb[i] * s.ArmIn;
            stations.Add(new(i, s.Role, RoleLabel(s.Role), stationLb[i], s.ArmIn));
        }
        double zeroFuel = weight;

        double fuelGal = Math.Clamp(r.FuelGal, 0, a.UsableFuelGal);
        double fuelLb = fuelGal * a.FuelLbPerGal;
        double toLb = zeroFuel + fuelLb;
        double toCg = (moment + fuelLb * a.FuelArmIn) / toLb;
        double landFuelGal = Math.Max(0, fuelGal - r.TripFuelGal);
        double landFuelLb = landFuelGal * a.FuelLbPerGal;
        double ldgLb = zeroFuel + landFuelLb;
        double ldgCg = (moment + landFuelLb * a.FuelArmIn) / ldgLb;

        double over = Math.Max(0, toLb - a.MaxGrossLb);
        if (over > 0)
            findings.Add(new(over > 110 ? FindingLevel.Stop : FindingLevel.Caution, "gross",
                F("{0:0} kg over maximum takeoff weight", Units.LbToKg(over))));
        double fwd = a.ForwardLimitAt(toLb);
        if (toCg > a.AftLimitIn + 0.05) findings.Add(new(FindingLevel.Stop, "aft", F("CG {0:0.0} in behind the aft limit, tail heavy", toCg - a.AftLimitIn)));
        if (toCg < fwd - 0.05) findings.Add(new(FindingLevel.Stop, "fwd", F("CG {0:0.0} in ahead of the forward limit", fwd - toCg)));
        if (ldgCg > a.AftLimitIn + 0.05 && toCg <= a.AftLimitIn + 0.05) findings.Add(new(FindingLevel.Stop, "aftland", F("CG moves behind the aft limit as fuel burns, {0:0.0} in at landing", ldgCg - a.AftLimitIn)));

        double toM = TakeoffDistanceM(a, toLb, r.Departure, r.OatC);
        double ldM = LandingDistanceM(a, ldgLb, r.Arrival, r.OatC);
        if (r.Departure is { } dep)
        {
            if (toM > dep.LengthM) findings.Add(new(FindingLevel.Stop, "todist", F("Needs {0:0} m to clear 50 ft, the strip has {1:0} m", toM, dep.LengthM)));
            else if (toM > dep.LengthM * 0.8) findings.Add(new(FindingLevel.Caution, "todist", F("Takeoff {0:0} m of {1:0} m, little margin", toM, dep.LengthM)));
        }
        if (r.Arrival is { } arr)
        {
            if (ldM > arr.LengthM) findings.Add(new(FindingLevel.Stop, "lddist", F("Needs {0:0} m to land, the strip has {1:0} m", ldM, arr.LengthM)));
            else if (ldM > arr.LengthM * 0.8) findings.Add(new(FindingLevel.Caution, "lddist", F("Landing {0:0} m of {1:0} m, little margin", ldM, arr.LengthM)));
        }

        return new LoadResult
        {
            Stations = stations, ZeroFuelLb = zeroFuel, FuelGal = fuelGal,
            TakeoffLb = toLb, TakeoffCgIn = toCg, LandingLb = ldgLb, LandingCgIn = ldgCg,
            MaxGrossLb = a.MaxGrossLb, ForwardLimitIn = fwd, AftLimitIn = a.AftLimitIn,
            OverGrossLb = over, OverflowBaggageLb = overflow,
            ExtraPayloadRoomLb = a.MaxGrossLb - toLb,
            TakeoffDistanceM = toM, LandingDistanceM = ldM, Findings = findings,
        };
    }

    /// <summary>Book distance scaled for weight, density altitude and surface. Rules of thumb, not a POH table.</summary>
    public static double TakeoffDistanceM(AircraftProfile a, double lb, RunwayInfo? rwy, double oatC)
    {
        double d = a.TakeoffOver50FtM * Math.Pow(lb / a.MaxGrossLb, 2.0);
        d *= DensityFactor(rwy?.ElevationFt ?? 0, oatC, 0.11);
        if (rwy?.Soft == true) d *= 1.15;
        return d;
    }

    public static double LandingDistanceM(AircraftProfile a, double lb, RunwayInfo? rwy, double oatC)
    {
        double d = a.LandingOver50FtM * (lb / a.MaxGrossLb);
        d *= DensityFactor(rwy?.ElevationFt ?? 0, oatC, 0.04);
        if (rwy?.Soft == true) d *= 1.2;
        return d;
    }

    private static double DensityFactor(double elevationFt, double oatC, double perThousandFt)
    {
        double isa = 15 - 1.98 * elevationFt / 1000.0;
        double densityAltFt = elevationFt + 120 * (oatC - isa);
        return Math.Max(0.8, 1 + perThousandFt * densityAltFt / 1000.0);
    }

    private static string RoleLabel(StationRole r) => r switch
    {
        StationRole.Pilot => "Pilot",
        StationRole.FrontSeat => "Front seat",
        StationRole.RearSeat => "Rear seat",
        StationRole.BaggageA => "Baggage A",
        StationRole.BaggageB => "Baggage B",
        _ => "Other",
    };
}

public sealed record FuelPlan(double DistanceNm, double GroundSpeedKt, double TripHours, double TripGal, double ReserveGal, double TaxiGal)
{
    public double RequiredGal => TripGal + ReserveGal + TaxiGal;
}

public static class FuelPlanner
{
    /// <param name="night">Night VFR needs 45 minutes in reserve, day 30.</param>
    public static FuelPlan Plan(AircraftProfile a, double distanceNm, double headwindKt = 0, bool night = false, double extraMinutes = 0)
    {
        double gs = Math.Max(40, a.CruiseKtas - headwindKt);
        double hours = distanceNm / gs + extraMinutes / 60.0 + (distanceNm > 1 ? 0.1 : 0);   // 6 minutes for the climb and the circuit
        double trip = hours * a.BurnGph;
        double reserve = (night ? 0.75 : 0.5) * a.BurnGph;
        return new FuelPlan(distanceNm, gs, hours, trip, reserve, a.TaxiFuelGal);
    }
}
