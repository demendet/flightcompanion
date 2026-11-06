using System.Text.Json;
using System.Text.Json.Serialization;
using FlightCompanion.Game.Missions;
using FlightCompanion.Game.People;
using FlightCompanion.Game.World;

namespace FlightCompanion.Game.Career;

public sealed record LedgerEntry(DateTimeOffset When, string Text, double AmountNok, double Balance);

public sealed record SmsMessage(DateTimeOffset When, string From, string Text);

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
    public int Seed { get; set; } = Random.Shared.Next();
    public int RandomCounter { get; set; }
    public List<Person> People { get; set; } = [];
    public List<Mission> Missions { get; set; } = [];
    public List<LedgerEntry> Ledger { get; set; } = [];
    public List<SmsMessage> Inbox { get; set; } = [];
    public List<PendingCall> PendingCalls { get; set; } = [];
    /// <summary>Gallons of your own fuel cached in sheds, by place. This is how you stretch your range out on the vidda.</summary>
    public Dictionary<string, double> FuelCache { get; set; } = new() { ["ENMP"] = 20 };
    public Dictionary<string, int> ArcProgress { get; set; } = [];
    public Dictionary<string, DateTimeOffset> ArcNextAt { get; set; } = [];
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

    public override Person PickCaller(MissionTemplate tpl, Random rng)
    {
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

        double agreed = m.Plan.PriceNok ?? 0;
        double pay = stars < 1.5 ? agreed * 0.5 : agreed;
        double wealth = lead?.Wealth ?? 0.5;
        double tip = stars >= 4.3 ? agreed * (0.03 + 0.2 * wealth) * (stars - 3.8) : 0;
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
            p.Trust = Math.Clamp(p.Trust + r.TrustDelta, -100, 100);
            p.LastContact = now;
        }
    }
}
