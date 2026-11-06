using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.Missions;
using FlightCompanion.Game.People;
using FlightCompanion.Game.World;

namespace FlightCompanion.Game.Career;

public abstract record DirectorAction;
/// <summary>A new job calls.</summary>
public sealed record RingNewJob(Mission Mission) : DirectorAction;
/// <summary>Someone you already know calls about something specific.</summary>
public sealed record RingAbout(Person Person, Mission? Mission, string Reason) : DirectorAction;
public sealed record TextMessage(Person Person, string Text) : DirectorAction;

public sealed record DirectorContext
{
    public required DateTimeOffset Now { get; init; }
    public bool InCall { get; init; }
    public bool Airborne { get; init; }
    public bool PassengersAboard { get; init; }
    public bool DoNotDisturb { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }
}

/// <summary>
/// Decides when the phone rings, so the world feels alive without burying you. Replaces the old 30 to 120 second timer.
/// Nothing here rings during a call, and nothing but an emergency rings while you have people on board.
/// </summary>
public sealed class Director(CareerState state, Random rng)
{
    private const int MaxOpenJobs = 3;

    public DirectorAction? Tick(DirectorContext ctx)
    {
        var now = ctx.Now;
        if (ctx.InCall) return null;

        // 1. Things you owe people come first. A forgotten customer is the best drama the game has.
        foreach (var m in state.OpenMissions.Where(x => x.Phase == MissionPhase.Booked && x.Plan.PickupTime is not null).ToList())
        {
            var due = m.Plan.PickupTime!.Value;
            var caller = state.PersonById(m.CallerId);
            if (caller is null) continue;
            double minutesLate = (now - due).TotalMinutes;

            if (minutesLate > 150)
            {
                m.Phase = MissionPhase.NoShow;
                caller.Trust = Math.Clamp(caller.Trust - 35, -100, 100);
                caller.Memories.Add(new MemoryNote(now, "The pilot never turned up at all, you had to make other arrangements."));
                state.Reputation = Math.Clamp(state.Reputation - 4, 0, 100);
                return new TextMessage(caller, L.F("We waited two and a half hours at {0}. We found another way. Do not call me.", m.Pickup?.Name));
            }
            if (minutesLate > 25 && !m.LateCallMade && !ctx.PassengersAboard)
            {
                m.LateCallMade = true;
                return new RingAbout(caller, m, $"The pilot was supposed to pick you up at {Clock.ToLocal(due):HH:mm} and it is now {Clock.ToLocal(now):HH:mm}. You are standing at {m.Pickup?.Name} with your bags, getting cold and annoyed. Ask where on earth they are.");
            }
            if (minutesLate > -40 && minutesLate < -15 && !m.ReminderSent)
            {
                m.ReminderSent = true;
                return new TextMessage(caller, L.F("See you at {0} at {1}. We are ready!", m.Pickup?.Name, Clock.ToLocal(due).ToString("HH:mm")));
            }
        }

        // 2. Calls that were promised for later, such as a referral or a follow up.
        var pending = state.PendingCalls.Where(p => p.Due <= now).OrderBy(p => p.Due).FirstOrDefault();
        if (pending is not null && !ctx.PassengersAboard && !ctx.DoNotDisturb)
        {
            state.PendingCalls.Remove(pending);
            if (state.PersonById(pending.PersonId) is { } who)
                return new RingAbout(who, state.MissionById(pending.MissionId), pending.Reason);
        }

        // 3. New work, with pacing that follows your reputation.
        state.NextCallAt ??= now.AddMinutes(1 + rng.NextDouble() * 2);
        if (now < state.NextCallAt.Value || ctx.DoNotDisturb) return null;
        if (ctx.PassengersAboard || state.OpenMissions.Count() >= MaxOpenJobs)
        {
            // Busy: an emergency still gets through to a pilot with an empty seat, everyone else tries later.
            state.NextCallAt = now.AddMinutes(3);
            return null;
        }
        return RollNewJob(ctx);
    }

    public Mission? ForceNewJob(DirectorContext ctx) => (RollNewJob(ctx) as RingNewJob)?.Mission;

    private DirectorAction? RollNewJob(DirectorContext ctx)
    {
        var now = ctx.Now;
        state.LastLat = ctx.Lat ?? state.LastLat;
        state.LastLon = ctx.Lon ?? state.LastLon;
        // A busy reputation means shorter gaps, from about 15 minutes at the start to about 5 at the top.
        double gap = (15 - state.Reputation / 10.0) * (0.6 + rng.NextDouble() * 0.8);
        state.NextCallAt = now.AddMinutes(Math.Max(3, gap));

        var arc = ArcDirector.NextEpisode(state, rng, now);
        if (arc is not null) return new RingNewJob(arc);

        var local = Clock.ToLocal(now);
        var eligible = MissionCatalog.Templates
            .Where(t => t.MinReputation <= state.Reputation)
            .Where(t => t.Months is null || t.Months.Contains(local.Month))
            .ToList();
        if (eligible.Count == 0) return null;

        // Variety: what you just flew becomes less likely, so hours of play do not feel like the same job.
        double Weight(MissionTemplate t)
        {
            int recent = state.CompletedTemplates.TakeLast(6).Count(x => x.StartsWith(t.Id));
            return t.Weight / (1 + recent * 1.5);
        }
        double total = eligible.Sum(Weight), roll = rng.NextDouble() * total;
        var tpl = eligible[^1];
        foreach (var t in eligible)
        {
            roll -= Weight(t);
            if (roll <= 0) { tpl = t; break; }
        }
        var mission = MissionFactory.Create(tpl, new CareerView(state), rng, now);
        state.Missions.Add(mission);
        return new RingNewJob(mission);
    }
}

public sealed record ArcEpisode(string TemplateId, string Premise, string TitleOverride, double MinReputation = 0, double DaysAfterPrevious = 0.02, string? Secret = null);

public sealed record StoryArc(string Id, string Title, Func<Person> Lead, ArcEpisode[] Episodes);

/// <summary>Longer stories, delivered between normal jobs. These are what you keep coming back for.</summary>
public static class ArcCatalog
{
    public static readonly IReadOnlyList<StoryArc> Arcs =
    [
        new("lodge_rebuild", "Rebuilding Mollisjok", () => new Person
        {
            Name = "Ingrid Somby", Nationality = "Sami", Language = "no", SpeaksNorwegian = true, Gender = "female", Age = 57,
            Job = "manager of the lodge at Mollisjok", WeightKg = 66, Fear = 0.05, Chatty = 0.6, Honesty = 0.95, Wealth = 0.45,
            Personality = "Runs the old timber lodge like a ship, tough, funny, always short of money and full of plans.",
            SpeechStyle = "Northern Norwegian, direct, teases you like family.",
        },
        [
            new("supply_lodge", "You run the lodge at Mollisjok. The roof leaked all winter and you want planks and roof fixings flown in from Lakselv, and you are testing whether this new pilot can be trusted with your supply runs.", "The lodge needs planks"),
            new("supply_lodge", "You need more planks and a load of tools at Mollisjok. The first load made a real difference, and you tell the pilot how the repair is going.", "More planks for the lodge", DaysAfterPrevious: 0.5),
            new("lodge_weekend", "The carpenters you hired need flying out to Mollisjok from Lakselv. You call on their behalf, with their tool cases, and you warn the pilot that carpenters always lie about their tools.", "Carpenters for the lodge", 30, 1, "The carpenters' tool cases weigh a lot more than they claim."),
            new("wedding", "The lodge has reopened and you are holding a party at Mollisjok to celebrate. You are inviting the pilot as well as asking for a flight with the guests and the cake.", "The lodge reopens", 40, 2),
        ]),
        new("salmon_society", "The Salmon Society", () => new Person
        {
            Name = "Klaus Reinhardt", Nationality = "German", Language = "en", Gender = "male", Age = 71, Job = "retired engineer who has fished here for 40 years",
            WeightKg = 94, Fear = 0.1, Chatty = 0.9, Honesty = 0.45, Wealth = 0.8,
            Personality = "Booming, generous, forever underestimating his gear. Part of a group of old friends who come every year.",
            SpeechStyle = "Heavy German accent, precise about fish and vague about everything else.",
        },
        [
            new("fishing_camp", "You and your old friends from the salmon society need flying out to the river as you have done for forty years. The old pilot retired, so you are trying this new one.", "The Salmon Society", 0, 0.02, "Your gear is far heavier than you admit. It always is."),
            new("fishing_camp", "You are back for another trip with your friends. You tell the pilot about the fish that got away last time. Walter, one of your friends, has not been feeling well but insisted on coming.", "The Society returns", 25, 3, "Walter is sicker than he lets on, and you are worried."),
            new("medevac", "Your friend Walter has collapsed at the camp. You are frightened and you need the pilot to come now. You have known him for 50 years.", "Walter", 30, 2),
            new("ashes", "Walter has died. He asked for his ashes to go on the river. You ask the pilot, because Walter liked them, to fly you there. You are subdued and grateful and you tell stories about him.", "A last cast for Walter", 30, 4),
        ]),
        new("reindeer_doctor", "The reindeer vet", () => new Person
        {
            Name = "Aslak Eira", Nationality = "Sami", Language = "no", SpeaksNorwegian = true, Gender = "male", Age = 44, Job = "reindeer veterinarian",
            WeightKg = 78, Fear = 0.0, Chatty = 0.3, Honesty = 1, Wealth = 0.4,
            Personality = "Dry and calm, spends his life outdoors, knows every herder on the plateau, trusts nobody until they prove themselves.",
            SpeechStyle = "Quiet northern Norwegian with long pauses, sly jokes.",
        },
        [
            new("reindeer_count", "You need to count herds around the plateau. You have heard about this pilot and you are checking whether they can fly low without scaring the animals.", "Counting the herds"),
            new("nurse_visit", "A herder's calf outbreak needs you at a remote site today, with your medicine crate. You speak to the pilot like someone you are starting to rely on.", "An outbreak on the plateau", 25, 1),
            new("medevac", "A young herder went through the ice. You found him, you stabilised him, but he needs a hospital. You are completely calm on the phone, which makes it worse.", "Through the ice", 40, 3),
        ]),
        new("the_inspector", "The inspector", () => new Person
        {
            Name = "Hege Lund", Nationality = "Norwegian", Language = "no", SpeaksNorwegian = true, Gender = "female", Age = 52, Job = "inspector from the Civil Aviation Authority",
            WeightKg = 70, Fear = 0.0, Chatty = 0.3, Honesty = 1, Wealth = 0.5,
            Personality = "Polite, thorough, impossible to charm. People have been talking about how you load your aircraft.",
            SpeechStyle = "Very correct, formal Norwegian. Notices everything.",
        },
        [
            new("family_visit", "You are from the Civil Aviation Authority. You have had reports of a local charter pilot flying overloaded. You book a flight as an ordinary passenger with an official bag you weigh yourself, and you say who you are only at the end. You will quietly check the weight sheet, the fuel, the briefing.", "A quiet passenger", 0, 0.02, "You are an inspector. You keep it hidden until after landing, then you give an honest verdict about what you saw."),
        ]),
    ];
}

public static class ArcDirector
{
    public static Mission? NextEpisode(CareerState state, Random rng, DateTimeOffset now)
    {
        foreach (var arc in ArcCatalog.Arcs.OrderBy(_ => rng.Next()))
        {
            if (arc.Id == "the_inspector" && state.OverweightFlights < 2) continue;
            int done = state.ArcProgress.GetValueOrDefault(arc.Id);
            if (done >= arc.Episodes.Length) continue;
            var ep = arc.Episodes[done];
            if (state.Reputation < ep.MinReputation) continue;
            if (state.ArcNextAt.TryGetValue(arc.Id, out var at) && now < at) continue;
            if (state.Missions.Any(m => m.ArcId == arc.Id && m.IsOpen)) continue;
            // Arcs are spice, not the meal: most of the time you get a normal job instead.
            if (state.FlightsCompleted < 1 || rng.NextDouble() > 0.3) continue;

            var lead = state.People.FirstOrDefault(p => p.ArcId == arc.Id);
            if (lead is null)
            {
                lead = arc.Lead();
                lead.ArcId = arc.Id;
                state.People.Add(lead);
            }
            var tpl = MissionCatalog.ById(ep.TemplateId);
            if (tpl is null) continue;
            var m = MissionFactory.Create(tpl, new CareerView(state), rng, now, lead);
            m.ArcId = arc.Id;
            m.ArcEpisode = done;
            m.Title = L.T(ep.TitleOverride);
            m.Premise = ep.Premise;
            if (ep.Secret is not null) m.SecretsForCaller.Add(ep.Secret);
            state.Missions.Add(m);
            return m;
        }
        return null;
    }

    public static void EpisodeFinished(CareerState state, Mission m, DateTimeOffset now)
    {
        if (m.ArcId is null) return;
        var arc = ArcCatalog.Arcs.FirstOrDefault(a => a.Id == m.ArcId);
        if (arc is null) return;
        int next = m.ArcEpisode + 1;
        state.ArcProgress[m.ArcId] = Math.Max(state.ArcProgress.GetValueOrDefault(m.ArcId), next);
        if (next < arc.Episodes.Length) state.ArcNextAt[m.ArcId] = now.AddDays(arc.Episodes[next].DaysAfterPrevious);
    }
}
