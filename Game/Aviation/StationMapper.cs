namespace FlightCompanion.Game.Aviation;

public sealed record SimStation(int SimIndex, string Name, StationRole Role);

/// <summary>
/// Different aircraft name and order their payload stations differently, so stations are matched by name.
/// The old code assumed stations 2 to 7 were seats and 9 was baggage, which is wrong for a 172.
/// </summary>
public static class StationMapper
{
    public static IReadOnlyList<SimStation> Map(IReadOnlyList<string> names)
    {
        var result = new List<SimStation>(names.Count);
        bool frontTaken = false, pilotTaken = false;
        int baggageSeen = 0;
        for (int i = 0; i < names.Count; i++)
        {
            string n = names[i].ToLowerInvariant();
            StationRole role;
            if (n.Contains("baggage") || n.Contains("cargo") || n.Contains("luggage") || n.Contains("freight") || n.Contains("tail"))
            {
                role = baggageSeen++ == 0 ? StationRole.BaggageA : StationRole.BaggageB;
            }
            else if ((n.Contains("pilot") && !n.Contains("co")) || n.Contains("captain") || (n.Contains("left") && n.Contains("front")))
            {
                role = pilotTaken ? StationRole.FrontSeat : StationRole.Pilot;
                pilotTaken = true;
            }
            else if (n.Contains("copilot") || n.Contains("co-pilot") || n.Contains("co pilot") || n.Contains("front"))
            {
                role = frontTaken ? StationRole.RearSeat : StationRole.FrontSeat;
                frontTaken = true;
            }
            else if (n.Contains("rear") || n.Contains("back") || n.Contains("passenger") || n.Contains("pax") || n.Contains("seat") || n.Contains("row"))
            {
                role = StationRole.RearSeat;
            }
            else role = StationRole.Other;
            result.Add(new SimStation(i + 1, names[i], role));
        }
        return result;
    }

    /// <summary>
    /// Turns the planned load into per-station pounds for the aircraft's real stations. The pilot station is left alone,
    /// because the sim manages it itself when your character walks around outside.
    /// </summary>
    public static IReadOnlyDictionary<int, double> ToSim(IReadOnlyList<SimStation> sim, IReadOnlyList<StationLoad> planned)
    {
        var writes = new Dictionary<int, double>();
        foreach (var s in sim.Where(s => s.Role != StationRole.Pilot && s.Role != StationRole.Other)) writes[s.SimIndex] = 0;

        void Assign(StationRole[] from, StationRole[] to)
        {
            var targets = sim.Where(s => to.Contains(s.Role)).ToList();
            if (targets.Count == 0) return;
            int i = 0;
            foreach (var load in planned.Where(p => from.Contains(p.Role) && p.Lb > 0))
            {
                var t = targets[Math.Min(i, targets.Count - 1)];
                writes[t.SimIndex] += load.Lb;
                i++;
            }
        }

        Assign([StationRole.FrontSeat], [StationRole.FrontSeat, StationRole.RearSeat]);
        // Rear passengers must not land on the front seat station already used.
        var rearTargets = sim.Where(s => s.Role == StationRole.RearSeat).ToList();
        if (rearTargets.Count > 0) Assign([StationRole.RearSeat], [StationRole.RearSeat]);
        else Assign([StationRole.RearSeat], [StationRole.FrontSeat]);

        var bagA = sim.FirstOrDefault(s => s.Role == StationRole.BaggageA);
        var bagB = sim.FirstOrDefault(s => s.Role == StationRole.BaggageB) ?? bagA;
        if (bagA is not null)
        {
            writes[bagA.SimIndex] += planned.Where(p => p.Role == StationRole.BaggageA).Sum(p => p.Lb);
            writes[bagB!.SimIndex] += planned.Where(p => p.Role == StationRole.BaggageB).Sum(p => p.Lb);
        }
        return writes;
    }

    /// <summary>Zero for everything we loaded, for when they get out.</summary>
    public static IReadOnlyDictionary<int, double> Empty(IReadOnlyList<SimStation> sim)
        => sim.Where(s => s.Role != StationRole.Pilot && s.Role != StationRole.Other).ToDictionary(s => s.SimIndex, _ => 0.0);
}

public static class FuelWrite
{
    /// <summary>
    /// Target fill level per tank. Always a level, never a quantity: measured on the C172, writing a quantity reads back about
    /// 1.5 gallons short, which once drained two synced aircraft to zero in seconds.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Levels(IReadOnlyDictionary<string, double> capacityGal, double totalGal)
    {
        double cap = capacityGal.Values.Sum();
        if (cap <= 0) return new Dictionary<string, double>();
        double level = Math.Clamp(totalGal / cap, 0, 1);
        return capacityGal.ToDictionary(k => k.Key, _ => level);
    }
}
