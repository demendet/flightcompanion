using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.Career;
using FlightCompanion.Game.World;

namespace FlightCompanion.Game.Missions;

public enum TripWish { Now, Soon, Later, Planned }

/// <summary>
/// When a trip happens. People call because they want to go, so nearly everything is now or within the hour, never days away,
/// and a booked time can be moved at any moment. Nobody can be picked up before the aircraft can physically get there.
/// </summary>
public static class TripTiming
{
    private const double StartUpMinutes = 5, UnknownMinutes = 15, AlreadyThereNm = 1.5;

    public static TripWish PickWish(MissionTemplate t, Random rng)
    {
        if (t.Urgent || t.Id == "vip_hurry") return TripWish.Now;
        if (t.Planned) return TripWish.Planned;
        double roll = rng.NextDouble();
        return roll < 0.6 ? TripWish.Now : roll < 0.9 ? TripWish.Soon : TripWish.Later;
    }

    public static string Sentence(TripWish wish, string from) => wish switch
    {
        TripWish.Now => $"You want to go right now. You are already at {from} with your things, or a few minutes away, and you can leave the moment the pilot gets there.",
        TripWish.Soon => $"You want to go within the next hour or two, and you can be at {from} quickly. If the pilot can come right now, that is even better.",
        TripWish.Later => $"You were thinking of this evening or tomorrow morning, but you can be ready at {from} much sooner if the pilot asks.",
        _ => $"It is coming up soon and you were thinking of tomorrow, but you can go from {from} today if the pilot offers.",
    };

    /// <summary>Flying time from where the aircraft is, with a few minutes to get going.</summary>
    public static double MinutesAway(double? lat, double? lon, Place? pickup, double cruiseKt)
    {
        if (lat is not { } la || lon is not { } lo || pickup is null) return UnknownMinutes;
        double nm = Geo.DistanceNm(la, lo, pickup.Lat, pickup.Lon);
        return nm < AlreadyThereNm ? 0 : nm / Math.Max(60, cruiseKt) * 60 + StartUpMinutes;
    }

    public static DateTimeOffset Earliest(DateTimeOffset now, double minutesAway) => now.AddMinutes(Math.Ceiling(minutesAway));

    /// <summary>The agreed time, unless it is impossible or nobody said one: then as soon as the aircraft can be there.</summary>
    public static DateTimeOffset Settle(DateTimeOffset? agreed, DateTimeOffset earliest) => agreed is { } a && a >= earliest ? a : earliest;

    /// <summary>What the caller knows about where you are. It makes "I can come now" mean something.</summary>
    public static string PilotLine(double minutesAway, string pickup) => minutesAway <= 0
        ? $"The pilot is already at {pickup}, parked and ready for you."
        : $"The pilot could be at {pickup} in about {Math.Ceiling(minutesAway):0} minutes.";

    /// <summary>A pickup later today when nobody agreed on a time for the trip back.</summary>
    public static DateTimeOffset DefaultReturn(DateTimeOffset now, Random rng)
    {
        var t = now.AddMinutes(120 + rng.NextDouble() * 120);
        return t.AddMinutes(15 - t.Minute % 15).AddSeconds(-t.Second);
    }

    /// <summary>Moves a booked pickup. Sooner is welcome. Every full hour you push it back costs some of their patience.</summary>
    public static DateTimeOffset Move(Mission m, CareerState c, DateTimeOffset wanted, DateTimeOffset earliest, DateTimeOffset now)
    {
        var old = m.Plan.PickupTime ?? now;
        var when = Settle(wanted, earliest);
        double change = (when - old).TotalMinutes;
        if (Math.Abs(change) < 1) return old;

        m.Plan.PickupTime = when;
        m.Plan.Changes.Add(new PlanChange(now, "time", old.ToString("ddd dd MMM HH:mm"), when.ToString("ddd dd MMM HH:mm")));
        m.LateCallMade = false;
        m.ReminderSent = false;

        int hoursBefore = (int)(m.DelayedMinutes / 60);
        m.DelayedMinutes = Math.Max(0, m.DelayedMinutes + change);
        int hoursAfter = (int)(m.DelayedMinutes / 60);
        if (c.PersonById(m.CallerId) is not { } who) return when;
        if (hoursAfter > hoursBefore)
        {
            who.Note(now, L.F("You pushed {0} back", m.Title), (hoursAfter - hoursBefore) * (m.Urgent ? -6 : -3));
            c.Inbox.Add(new SmsMessage(now, who.Name, L.F("Now {0}? We have other things to do than wait for you.", Clock.ToLocal(when).ToString("HH:mm"))));
        }
        else if (change < 0 && (old - now).TotalMinutes > 15 && (when - now).TotalMinutes <= 15)
            c.Inbox.Add(new SmsMessage(now, who.Name, L.T("Good, we are on our way.")));
        return when;
    }
}
