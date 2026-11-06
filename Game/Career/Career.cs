using System.Text.Json;
using System.Text.Json.Serialization;
using FlightCompanion.Game.Missions;
using FlightCompanion.Game.People;
using FlightCompanion.Game.World;

namespace FlightCompanion.Game.Career;

public sealed record LedgerEntry(DateTimeOffset When, string Text, double AmountNok, double Balance);

public sealed record SmsMessage(DateTimeOffset When, string From, string Text);

/// <summary>One whole conversation, word for word. People remember these, and the logbook shows them.</summary>
public sealed class TalkRecord
{
    public DateTimeOffset When { get; set; }
    public string Mode { get; set; } = "";
    public List<string> PersonIds { get; set; } = [];
    public List<string> Names { get; set; } = [];
    public string? MissionTitle { get; set; }
    public List<string> Lines { get; set; } = [];
    public bool HungUp { get; set; }
    public int Offence { get; set; }
}

public sealed record PendingCall(DateTimeOffset Due, string PersonId, string Reason, string? MissionId = null);

public sealed class CareerState
{
    public int Version { get; set; } = 1;
    public string CompanyName { get; set; } = "Vidda Air Charter";
    public string PilotName { get; set; } = "Captain";
    public double PilotKg { get; set; } = 85;
    public double CashNok { get; set; } = 40000;
    public double LoanNok { get; set; } = 120000;
    /// <summary>0 to 100. What people up here say about you.</summary>
    public double Reputation { get; set; } = 30;
    public double HoursFlown { get; set; }
    public int FlightsCompleted { get; set; }
    public int OverweightFlights { get; set; }
    public int OffencesReported { get; set; }
    public int Seed { get; set; } = Random.Shared.Next();
    public int RandomCounter { get; set; }
    public List<Person> People { get; set; } = [];
    public List<Mission> Missions { get; set; } = [];
    public List<LedgerEntry> Ledger { get; set; } = [];
    public List<SmsMessage> Inbox { get; set; } = [];
    public List<PendingCall> PendingCalls { get; set; } = [];
    public List<TalkRecord> Talks { get; set; } = [];
    /// <summary>Gallons of your own fuel cached in sheds, by place. This is how you stretch your range out on the vidda.</summary>
    public Dictionary<string, double> FuelCache { get; set; } = new() { ["ENMP"] = 20 };
    public Dictionary<string, int> ArcProgress { get; set; } = [];
    public Dictionary<string, DateTimeOffset> ArcNextAt { get; set; } = [];
    /// <summary>The person who speaks with the player's own cloned voice. They call a little more often than anyone else.</summary>
    public string? FavoriteCallerId { get; set; }
    public DateTimeOffset? NextCallAt { get; set; }
    public DateTimeOffset? LastLoanPayment { get; set; }
    public double? LastLat { get; set; }
    public double? LastLon { get; set; }
    public List<string> CompletedTemplates { get; set; } = [];

    public Person? PersonById(string? id) => People.FirstOrDefault(p => p.Id == id);
    public Mission? MissionById(string? id) => Missions.FirstOrDefault(m => m.Id == id);
    public IEnumerable<Mission> OpenMissions => Missions.Where(m => m.IsOpen);

    public void Book(DateTimeOffset now, string text, double amount)
    {
        CashNok += amount;
        Ledger.Add(new LedgerEntry(now, text, amount, CashNok));
    }

    public string RankName => Reputation switch
    {
        < 15 => L.T("Nobody has heard of you"),
        < 30 => L.T("That new pilot"),
        < 50 => L.T("Known around Karasjok"),
        < 70 => L.T("The one you call"),
        < 88 => L.T("A legend on the vidda"),
        _ => L.T("They name the strip after you"),
    };
}

public sealed class CareerView(CareerState state) : CareerStateView
{
    public override double? CurrentLat => state.LastLat;
    public override double? CurrentLon => state.LastLon;

    /// <summary>A bit over one new job in six comes from the favorite, as long as they still talk to you and are not already booked.</summary>
    public const double FavoriteChance = 0.18;

    public override Person PickCaller(MissionTemplate tpl, Random rng)
    {
        if (state.PersonById(state.FavoriteCallerId) is { Trust: > -40 } favorite
            && tpl.CallerNationality is null or "Sami" or "Norwegian"
            && !state.Missions.Any(m => m.CallerId == favorite.Id && (m.IsOpen || m.Phase is MissionPhase.Ringing or MissionPhase.Negotiating))
            && rng.NextDouble() < FavoriteChance)
            return favorite;

        // Regulars come back. The better your name, the more of your work comes from people you already know.
        double regularChance = Math.Clamp(0.15 + state.Reputation / 180.0, 0.1, 0.65);
        var regulars = state.People.Where(p => p.FlightsWithYou > 0 && p.Trust > -25 && p.ArcId is null
            && (tpl.CallerNationality is null || p.Nationality == tpl.CallerNationality)).ToList();
        if (regulars.Count > 0 && rng.NextDouble() < regularChance)
            return regulars[rng.Next(regulars.Count)];

        var p = CastFactory.Create(rng, tpl.CallerNationality, tpl.CallerJob);
        // A happy customer sends a friend along.
        var fans = state.People.Where(x => x.Trust > 25 && x.FlightsWithYou > 0).ToList();
        if (fans.Count > 0 && rng.NextDouble() < 0.35)
            p.ReferredBy = fans[rng.Next(fans.Count)].Name;
        state.People.Add(p);
        return p;
    }

    public override Person NewCompanion(Person lead, Random rng)
    {
        var c = CastFactory.Companion(rng, lead);
        state.People.Add(c);
        return c;
    }
}

public static class CareerStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static CareerState Load(string path)
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (File.Exists(candidate))
                    return JsonSerializer.Deserialize<CareerState>(File.ReadAllText(candidate), Options) ?? new CareerState();
            }
            catch (JsonException) { /* a broken save falls back to the backup below */ }
        }
        return new CareerState();
    }

    /// <summary>Writes to a temp file, keeps the last good one as .bak, so a crash cannot lose the career.</summary>
    public static void Save(CareerState state, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, Options));
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
        else File.Move(tmp, path);
    }
}

public static class Offences
{
    private const double ApologyTrust = 12, ForgivenAtMost = 0;

    /// <summary>
    /// An offence counts only with the pilot's own words behind it. The note taker once flagged a customer retelling what
    /// happened, which ended the very call the pilot made to apologise.
    /// </summary>
    public static bool SaidByPilot(string? quote, IEnumerable<string> pilotLines)
    {
        static string Plain(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        string wanted = Plain(quote ?? "");
        return wanted.Length > 0 && pilotLines.Any(l => Plain(l).Contains(wanted));
    }

    /// <summary>Saying sorry wins some trust back, one call at a time, but it never turns a grudge into friendship.</summary>
    public static void Apologise(IReadOnlyList<Person> present, DateTimeOffset now)
    {
        foreach (var p in present.Where(x => x.Trust < ForgivenAtMost))
        {
            p.Note(now, L.T("You said sorry"), Math.Min(ApologyTrust, ForgivenAtMost - p.Trust));
            p.Memories.Add(new MemoryNote(now, L.T("The pilot called and said sorry for how you were treated.")));
        }
    }

    /// <summary>
    /// 1 is rude. 2 is harassment, threats or a sexual proposition, including offering a flight in return. Nobody ever accepts:
    /// they refuse, the job is off if it can be, and word gets around.
    /// </summary>
    public static void Apply(CareerState c, Mission? m, IReadOnlyList<Person> present, string what, int severity, DateTimeOffset now)
    {
        bool serious = severity >= 2;
        double change = serious ? -45 : -8;
        foreach (var p in present)
        {
            p.Note(now, what, change, serious);
            p.Memories.Add(new MemoryNote(now, what));
        }
        if (m is not null)
        {
            m.PilotOffended = true;
            if (serious && m.Phase is MissionPhase.Ringing or MissionPhase.Negotiating or MissionPhase.Booked or MissionPhase.AtPickup or MissionPhase.Boarding)
                m.Phase = MissionPhase.Cancelled;
        }
        if (!serious) return;
        c.Reputation = Math.Clamp(c.Reputation - 6, 0, 100);
        c.OffencesReported++;
        // People up here talk. Those you have dealt with hear about it.
        foreach (var other in c.People.Where(x => !present.Contains(x) && (x.Talks > 0 || x.FlightsWithYou > 0)))
            other.Note(now, L.T("Heard how you treated a customer"), -4);
        if (present.Count > 0)
            c.Inbox.Add(new SmsMessage(now, present[0].Name, L.T("Never contact me again. I am telling people what you said.")));
    }
}

/// <summary>Wipes people from the player's history, one at a time or everyone. Your own money, logbook and reputation stay as they are.</summary>
public static class Forgetting
{
    /// <summary>Someone in the middle of a talk or on board cannot vanish.</summary>
    public static bool InUse(CareerState c, Person p) => c.Missions.Any(m =>
        m.Phase is MissionPhase.Negotiating or MissionPhase.AtPickup or MissionPhase.Boarding or MissionPhase.Loaded or MissionPhase.Airborne or MissionPhase.Landed
        && (m.CallerId == p.Id || m.CompanionIds.Contains(p.Id)));

    /// <summary>They stay in the world, but as if you had never met.</summary>
    public static void Reset(Person p, DateTimeOffset now)
    {
        p.Trust = 0;
        p.FlightsWithYou = 0;
        p.Talks = 0;
        p.ReferredBy = null;
        p.LastContact = null;
        p.Memories.Clear();
        p.Record.Clear();
        p.ForgottenAt = now;
    }

    /// <summary>Gone for good. Anything booked with them is called off, at no cost to you.</summary>
    public static void Delete(CareerState c, Person p)
    {
        foreach (var m in c.Missions.Where(m => m.Phase == MissionPhase.Booked && (m.CallerId == p.Id || m.CompanionIds.Contains(p.Id))))
            m.Phase = MissionPhase.Cancelled;
        c.PendingCalls.RemoveAll(x => x.PersonId == p.Id);
        c.Talks.RemoveAll(t => t.PersonIds.Contains(p.Id));
        if (c.FavoriteCallerId == p.Id) c.FavoriteCallerId = null;
        c.People.Remove(p);
    }

    public static int ResetEveryone(CareerState c, DateTimeOffset now)
    {
        int n = 0;
        foreach (var p in c.People.Where(p => !InUse(c, p) && (p.Talks > 0 || p.FlightsWithYou > 0 || p.Memories.Count > 0 || p.Record.Count > 0)))
        {
            Reset(p, now);
            n++;
        }
        return n;
    }
}

public static class Cancellation
{
    public static bool Allowed(Mission m) => m.Phase is MissionPhase.Booked or MissionPhase.AtPickup or MissionPhase.Boarding;

    /// <summary>Calling it off is always your right, but the closer to the pickup, the worse it lands. Days ahead costs almost nothing.</summary>
    public static (double Trust, double Reputation) Cost(Mission m, DateTimeOffset now)
    {
        double hours = m.Plan.PickupTime is { } due ? (due - now).TotalHours : 24;
        if (m.Phase != MissionPhase.Booked || hours < 0) return (-20, -2);
        if (hours < 2) return (-14, -1);
        if (hours < 24) return (-6, -0.3);
        return (-2, 0);
    }

    public static void Apply(Mission m, CareerState c, DateTimeOffset now)
    {
        var (trust, reputation) = Cost(m, now);
        m.Phase = MissionPhase.Cancelled;
        c.Reputation = Math.Clamp(c.Reputation + reputation, 0, 100);
        if (c.PersonById(m.CallerId) is { } who)
        {
            who.Note(now, L.F("You cancelled {0}", m.Title), trust);
            who.Memories.Add(new MemoryNote(now, L.F("Piloten avlyste turen til {0}.", m.Dropoff?.Name)));
            string text = trust <= -14 ? L.T("You cancelled now? We are standing here with our bags. Thanks for nothing.")
                : trust <= -6 ? L.T("That was short notice. We will have to find something else.")
                : L.T("Fine, thanks for letting us know in time.");
            c.Inbox.Add(new SmsMessage(now, who.Name, text));
        }
    }
}

public static class ReviewMaker
{
    public static FlightReview Make(Mission m, CareerState c, Person? lead, DateTimeOffset now, double fuelPriceNok = 118)
    {
        var s = m.Score;
        var points = new List<string>();
        var grade = Flight.LandingScale.Grade(s.TouchdownFpm, s.Bounces);
        double late = 0;
        if (m.Plan.PickupTime is { } due && m.ArrivedAtPickup is { } arrived)
            late = Math.Max(0, (arrived - due).TotalMinutes);

        double comfortAvg = s.ComfortSamples > 0 ? s.ComfortAvgSum / s.ComfortSamples : 100;
        double stars = 5;
        stars -= (100 - comfortAvg) / 22;
        stars -= late switch { > 90 => 1.8, > 45 => 1.0, > 15 => 0.5, _ => 0 };
        stars -= grade switch { Flight.LandingGrade.Crash => 3, Flight.LandingGrade.Hard => 1.2, Flight.LandingGrade.Firm => 0.3, _ => 0 };
        stars -= s.StallWarnings * 0.4;
        if (!s.LandedAtDestination) stars -= 1.5;
        stars -= m.Stops.Count(x => !x.Seen) * 1.3;
        if (m.PilotOffended) stars -= 3;
        if (m.WentOverweight) stars -= 0.2;
        stars = Math.Clamp(stars, 0.5, 5);

        if (late > 15) points.Add(L.F("{0:0} minutes late to the pickup", late));
        else if (m.ArrivedAtPickup is not null) points.Add(L.T("On time"));
        points.Add(L.T(Flight.LandingScale.Words(grade)));
        if (s.StallWarnings > 0) points.Add(L.F("Stall horn {0} times", s.StallWarnings));
        if (s.ComfortMin < 40) points.Add(L.T("Passengers were frightened"));
        else if (comfortAvg > 88) points.Add(L.T("A gentle flight"));
        if (m.WentOverweight) points.Add(L.F("Flew {0:0} kg over maximum weight", Aviation.Units.LbToKg(m.LoadedOverGrossLb)));
        if (!s.LandedAtDestination) points.Add(L.T("Did not land where agreed"));
        foreach (var missed in m.Stops.Where(x => !x.Seen)) points.Add(L.F("Never flew to {0}", missed.Name));
        if (m.PilotOffended) points.Add(L.T("You behaved badly towards them"));

        double agreed = m.Plan.PriceNok ?? 0;
        double pay = stars < 1.5 ? agreed * 0.5 : agreed;
        double wealth = lead?.Wealth ?? 0.5;
        double tip = stars >= 4.3 && !m.PilotOffended ? agreed * (0.03 + 0.2 * wealth) * (stars - 3.8) : 0;
        double repair = grade switch { Flight.LandingGrade.Crash => 60000, Flight.LandingGrade.Hard => 9000, _ => 0 };
        if (m.WentOverweight) repair += Math.Min(4000, m.LoadedOverGrossLb * 15);
        double fuelCost = s.FuelBurnedGal * fuelPriceNok;
        double rep = (stars - 3.2) * 1.8 + (m.Urgent ? 1.2 : 0);
        double trust = (stars - 3) * 9;

        return new FlightReview
        {
            Stars = Math.Round(stars * 2) / 2, PayNok = Math.Round(pay), TipNok = Math.Round(tip / 10) * 10, RepairNok = repair,
            FuelCostNok = Math.Round(fuelCost), ReputationDelta = Math.Round(rep, 1), TrustDelta = Math.Round(trust, 1),
            LateMinutes = Math.Round(late), LandingVerdict = Flight.LandingScale.Words(grade), Points = points,
        };
    }

    /// <summary>Everything that happens the moment a mission closes: money, standing, memory.</summary>
    public static void Settle(Mission m, FlightReview r, CareerState c, DateTimeOffset now)
    {
        m.Review = r;
        m.Phase = MissionPhase.Complete;
        c.Book(now, L.F("{0}: fare", m.Title), r.PayNok);
        if (r.TipNok > 0) c.Book(now, L.F("{0}: tip", m.Title), r.TipNok);
        if (r.RepairNok > 0) c.Book(now, L.F("{0}: mechanic", m.Title), -r.RepairNok);
        c.Reputation = Math.Clamp(c.Reputation + r.ReputationDelta, 0, 100);
        c.FlightsCompleted++;
        c.HoursFlown += m.Score.AirborneHours;
        c.CompletedTemplates.Add(m.TemplateId);
        if (m.WentOverweight) c.OverweightFlights++;
        foreach (var id in new[] { m.CallerId }.Concat(m.CompanionIds))
        {
            if (c.PersonById(id) is not { } p) continue;
            p.FlightsWithYou++;
            p.Note(now, L.F("{0}: {1:0.0} of 5", m.Title, r.Stars), r.TrustDelta);
        }
    }
}
