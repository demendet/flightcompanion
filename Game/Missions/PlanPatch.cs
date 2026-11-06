using FlightCompanion.Game.World;

namespace FlightCompanion.Game.Missions;

/// <summary>
/// What the note taker heard in the conversation so far. Null means nothing was said about it.
/// The note taker never gets to invent anything, and code decides what the numbers mean.
/// </summary>
public sealed record PlanPatch
{
    public int? PassengerCount { get; init; }
    public List<string>? PassengerNames { get; init; }
    public double? PassengerWeightEachKg { get; init; }
    public double? BaggageTotalKg { get; init; }
    public string? BaggageNote { get; init; }
    public double? CargoKg { get; init; }
    public string? CargoNote { get; init; }
    public string? Pickup { get; init; }
    public string? Dropoff { get; init; }
    /// <summary>ISO 8601 local date and time, Europe/Oslo.</summary>
    public string? PickupTimeLocal { get; init; }
    /// <summary>ISO 8601 local date and time for a later pickup on the way back, when one was agreed.</summary>
    public string? ReturnTimeLocal { get; init; }
    public double? PriceNok { get; init; }
    public bool? BothAgreed { get; init; }
    public bool? PilotDeclined { get; init; }
    public bool? CallIsOver { get; init; }
    public string? NewNote { get; init; }
}

public static class PlanPatcher
{
    /// <summary>Applies the patch and records each real change, so the card can flash what just moved.</summary>
    public static IReadOnlyList<PlanChange> Apply(MissionPlan plan, PlanPatch p, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        var changes = new List<PlanChange>();
        void Note(string f, string from, string to)
        {
            var c = new PlanChange(now, f, from, to);
            changes.Add(c);
            plan.Changes.Add(c);
        }

        if (p.Pickup is not null && WorldData.Find(p.Pickup) is { } from && from.Id != plan.PickupId)
        {
            Note("pickup", WorldData.ById(plan.PickupId)?.Name ?? "-", from.Name);
            plan.PickupId = from.Id;
        }
        if (p.Dropoff is not null && WorldData.Find(p.Dropoff) is { } to && to.Id != plan.DropoffId)
        {
            Note("dropoff", WorldData.ById(plan.DropoffId)?.Name ?? "-", to.Name);
            plan.DropoffId = to.Id;
        }
        if (p.PassengerCount is { } n and >= 0 and <= 12 && n != plan.Passengers.Count)
        {
            Note("passengers", plan.Passengers.Count.ToString(), n.ToString());
            while (plan.Passengers.Count < n) plan.Passengers.Add(new PlanPassenger { Name = $"Passenger {plan.Passengers.Count + 1}" });
            while (plan.Passengers.Count > n) plan.Passengers.RemoveAt(plan.Passengers.Count - 1);
        }
        if (p.PassengerNames is { Count: > 0 } names)
        {
            for (int i = 0; i < names.Count && i < plan.Passengers.Count; i++)
                if (!string.IsNullOrWhiteSpace(names[i]) && plan.Passengers[i].PersonId is null) plan.Passengers[i].Name = names[i];
        }
        if (p.PassengerWeightEachKg is { } each and > 20 and < 250)
        {
            foreach (var x in plan.Passengers.Where(x => !x.KgKnown || Math.Abs(x.Kg - each) > 0.5))
            {
                x.Kg = each;
                x.KgKnown = true;
            }
        }
        if (p.BaggageTotalKg is { } bag and >= 0 and < 2000 && Math.Abs(bag - plan.BaggageKg) >= 0.5 && !plan.BaggageWeighed)
        {
            Note("baggage", $"{plan.BaggageKg:0} kg", $"{bag:0} kg");
            plan.BaggageKg = bag;
        }
        if (!string.IsNullOrWhiteSpace(p.BaggageNote)) plan.BaggageNote = p.BaggageNote!;
        if (p.CargoKg is { } cargo and >= 0 and < 2000 && Math.Abs(cargo - plan.CargoKg) >= 0.5)
        {
            Note("cargo", $"{plan.CargoKg:0} kg", $"{cargo:0} kg");
            plan.CargoKg = cargo;
        }
        if (!string.IsNullOrWhiteSpace(p.CargoNote)) plan.CargoNote = p.CargoNote!;
        if (p.PickupTimeLocal is not null && ParseLocal(p.PickupTimeLocal, zone) is { } when
            && (plan.PickupTime is null || Math.Abs((plan.PickupTime.Value - when).TotalMinutes) >= 1))
        {
            Note("time", plan.PickupTime?.ToString("ddd dd MMM HH:mm") ?? "-", when.ToString("ddd dd MMM HH:mm"));
            plan.PickupTime = when;
        }
        if (p.ReturnTimeLocal is not null && ParseLocal(p.ReturnTimeLocal, zone) is { } back
            && (plan.ReturnTime is null || Math.Abs((plan.ReturnTime.Value - back).TotalMinutes) >= 1))
        {
            Note("return", plan.ReturnTime?.ToString("ddd dd MMM HH:mm") ?? "-", back.ToString("ddd dd MMM HH:mm"));
            plan.ReturnTime = back;
        }
        if (p.PriceNok is { } price and >= 0 && (plan.PriceNok is null || Math.Abs(plan.PriceNok.Value - price) >= 1))
        {
            Note("price", plan.PriceNok?.ToString("0") ?? "-", price.ToString("0"));
            plan.PriceNok = price;
        }
        if (p.BothAgreed is { } agreed && agreed != plan.Agreed)
        {
            Note("agreed", plan.Agreed ? "yes" : "no", agreed ? "yes" : "no");
            plan.Agreed = agreed;
        }
        if (!string.IsNullOrWhiteSpace(p.NewNote) && !plan.Notes.Contains(p.NewNote!)) plan.Notes.Add(p.NewNote!);
        return changes;
    }

    public static DateTimeOffset? ParseLocal(string text, TimeZoneInfo? zone)
    {
        if (!DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var local)) return null;
        zone ??= Clock.Zone;
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }
}

public static class Clock
{
    /// <summary>The game runs on real Norwegian time: a pickup on Saturday at 14:00 means this actual Saturday.</summary>
    public static readonly TimeZoneInfo Zone = FindZone();

    private static TimeZoneInfo FindZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo"); } catch { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); } catch { }
        return TimeZoneInfo.Local;
    }

    public static DateTimeOffset ToLocal(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, Zone);
}
