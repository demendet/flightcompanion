using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using FlightCompanion.Game;
using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.Career;
using FlightCompanion.Game.Missions;
using FlightCompanion.Game.Talk;
using FlightCompanion.Game.World;
using FlightCompanion.Services;

namespace FlightCompanion.ViewModels;

public sealed record StatusRow(string Name, string Text, IBrush Dot);
public sealed record ChatRow(string Who, string Text, bool FromPilot, IBrush WhoBrush);
public sealed record JobRow(string Id, string Title, string When, string Route, string Status, IBrush StatusBrush, bool Selected);
public sealed record SmsRow(string When, string From, string Text);
public sealed record BarRow(string Label, double Value, string Text, IBrush Brush);
public sealed record FactRow(string Label, string Value);
public sealed record MemoryRow(string When, string Text);
public sealed record PersonFlightRow(string When, string Title, string Route, string Outcome, string Money);

public sealed record PersonRow
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Initials { get; init; }
    public required IBrush AvatarBrush { get; init; }
    public required string Line { get; init; }
    public required string Standing { get; init; }
    public required IBrush StandingBrush { get; init; }
    public required double TrustValue { get; init; }
    public required string Memory { get; init; }
    public required bool HasMemory { get; init; }
    public required string QuickFacts { get; init; }
    public required string CallLabel { get; init; }
    public required string Personality { get; init; }
    public required IReadOnlyList<BarRow> Bars { get; init; }
    public required IReadOnlyList<FactRow> Facts { get; init; }
    public required IReadOnlyList<MemoryRow> Memories { get; init; }
    public required bool NoMemories { get; init; }
    public required IReadOnlyList<PersonFlightRow> Flights { get; init; }
    public required bool NoFlights { get; init; }
    public required IReadOnlyList<RecordRow> Record { get; init; }
    public required bool NoRecord { get; init; }
    public required bool HasSerious { get; init; }
    public required string SeriousLine { get; init; }
    public required double TrustRaw { get; init; }
    public required int FlightCount { get; init; }
}
public sealed record LogRow(string When, string Title, string Route, string Stars, string StarSigns, string Money, string Quote, IReadOnlyList<string> Points, IBrush MoneyBrush);
public sealed record StatRow(string Value, string Label);
public sealed record TalkLine(string Who, string Text, IBrush Brush);
public sealed record TalkRow(string When, string Names, string What, string Flag, IBrush FlagBrush, bool HasFlag, bool Offence, string Count, IReadOnlyList<TalkLine> Lines);
public sealed record LedgerRow(string When, string Text, string Amount, IBrush AmountBrush);
public sealed record SeatRow(string Label, string Who, string Kg, bool Guess);
public sealed record FindingRow(string Text, IBrush Brush);
public sealed record ChangeRow(string Text);

public sealed record EnvelopeData(AircraftProfile Profile, double TakeoffLb, double TakeoffCg, double LandingLb, double LandingCg, double? LiveLb, double? LiveCg, bool Legal);

public sealed record MapMarker(string Name, double Lat, double Lon, bool Remote, bool LabelLeft, bool Airport, double RunwayM, bool HasFuel);
public sealed record MapName(string Name, double Lat, double Lon);
public sealed record PlanePos(double Lat, double Lon, double Heading, double ReachNm, IReadOnlyList<(double Lat, double Lon)> Trail, double? NavLat, double? NavLon);
public sealed record MapStop(string Name, double Lat, double Lon, bool Seen);
public sealed record MapData(IReadOnlyList<MapMarker> Places, Place? From, Place? To, IReadOnlyList<MapStop> Stops, IReadOnlyList<MapName> Sights, IReadOnlyList<MapName> Known, double CruiseKt);
public sealed record RecordRow(string When, string Text, string Change, IBrush ChangeBrush);
public sealed record RouteRow(string Text, IBrush Brush);

public sealed record FuelPicture
{
    public required string OnBoard { get; init; }
    public required string Here { get; init; }
    public required string Need { get; init; }
    public required string Warning { get; init; }
    public required bool HasWarning { get; init; }
    public required bool HasOneWay { get; init; }
    public required bool HasRoundTrip { get; init; }
    public required string OneWayLabel { get; init; }
    public required string RoundTripLabel { get; init; }
    public required double OneWayTarget { get; init; }
    public required double RoundTripTarget { get; init; }
    public required string FullLabel { get; init; }
    public required double FullTarget { get; init; }
    public required bool CanFuel { get; init; }
}

public sealed record CardPicture
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Route { get; init; }
    public required string When { get; init; }
    public required string Price { get; init; }
    public required string Phase { get; init; }
    public required string DistanceLine { get; init; }
    public required string MeetLine { get; init; }
    public required string Weather { get; init; }
    public required IReadOnlyList<SeatRow> Seats { get; init; }
    public required string TakeoffWeight { get; init; }
    public required string Room { get; init; }
    public required IBrush RoomBrush { get; init; }
    public required string FuelLine { get; init; }
    public required string StripLine { get; init; }
    public required double WeightFraction { get; init; }
    public required double FuelFraction { get; init; }
    public required double FuelRequiredFraction { get; init; }
    public required IReadOnlyList<FindingRow> Findings { get; init; }
    public required IReadOnlyList<ChangeRow> Changes { get; init; }
    public required string Notes { get; init; }
    public required bool HasChanges { get; init; }
    public required EnvelopeData Envelope { get; init; }
    public required bool CanCall { get; init; }
    public required bool CanMeet { get; init; }
    public required bool CanWeigh { get; init; }
    public required bool CanBoard { get; init; }
    public required bool CanDeplane { get; init; }
    public required bool CanCancel { get; init; }
    public required bool CanMoveTime { get; init; }
    public required DateTimeOffset? PickupAt { get; init; }
    public required string BoardLabel { get; init; }
    public required bool HasReview { get; init; }
    public required string ReviewLine { get; init; }
    public required string ReviewPoints { get; init; }
    public required string ReviewQuote { get; init; }
}

public sealed record Picture
{
    public required string Company { get; init; }
    public required string Cash { get; init; }
    public required IBrush CashBrush { get; init; }
    public required string Rank { get; init; }
    public required double Reputation { get; init; }
    public required string Career { get; init; }
    public required IReadOnlyList<StatusRow> Statuses { get; init; }
    public required bool NeedKeys { get; init; }

    public required bool IsRinging { get; init; }
    public required bool IsTalking { get; init; }
    public required bool IsIdle { get; init; }
    public required string PhoneTitle { get; init; }
    public required string PhoneSub { get; init; }
    public required string Partial { get; init; }
    public required bool HasPartial { get; init; }
    public required IReadOnlyList<ChatRow> Chat { get; init; }
    public required string HangUpLabel { get; init; }
    public required string MicTestLabel { get; init; }
    public required string Timing { get; init; }

    public required CardPicture? Card { get; init; }
    public required bool NoCard { get; init; }
    public required IReadOnlyList<JobRow> Jobs { get; init; }
    public required bool NoJobs { get; init; }
    public required IReadOnlyList<SmsRow> Inbox { get; init; }
    public required IReadOnlyList<PersonRow> People { get; init; }
    public required bool NoPeople { get; init; }
    public required string PeopleSummary { get; init; }
    public required IReadOnlyList<StatRow> LogStats { get; init; }
    public required IReadOnlyList<TalkRow> Talks { get; init; }
    public required bool NoFlights { get; init; }
    public required bool NoTalks { get; init; }
    public required IReadOnlyList<LogRow> Logbook { get; init; }
    public required IReadOnlyList<LedgerRow> Ledger { get; init; }
    public required MapData Map { get; init; }
    public required string FuelCache { get; init; }
    public required IReadOnlyList<RouteRow> Route { get; init; }
    public required FuelPicture Fuel { get; init; }
}

public static class Palette
{
    public static readonly IBrush Good = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#7FCB8F"));
    public static readonly IBrush Caution = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#F4C430"));
    public static readonly IBrush Stop = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#EE6A5E"));
    public static readonly IBrush Quiet = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#7F9C95"));
    public static readonly IBrush Text = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#E6EEE9"));

    public static IBrush For(HealthLevel l) => l switch
    {
        HealthLevel.Good => Good,
        HealthLevel.Working => Caution,
        HealthLevel.Problem => Stop,
        _ => Quiet,
    };
}

/// <summary>Builds what the screen shows, on the game loop, so the UI thread never walks a list the game is changing.</summary>
public static class PictureBuilder
{
    private static string Nok(double v) => $"{v:N0} kr".Replace(",", " ").Replace(" ", " ");
    private static System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo(L.Norwegian ? "nb-NO" : "en-GB");
    private static string LocalTime(DateTimeOffset t) => Clock.ToLocal(t).ToString("ddd d MMM HH:mm", Culture);

    public static Picture Build(GameHost h, string? selectedMissionId)
    {
        var c = h.Career;
        var now = DateTimeOffset.Now;
        var title = h.Latest?.Title;
        var aircraft = h.CurrentAircraft();

        var focus = h.TalkMission ?? h.ActiveMission ?? h.Ringing?.Mission
            ?? c.MissionById(selectedMissionId)
            ?? c.Missions.FirstOrDefault(m => m.Phase == MissionPhase.Negotiating)
            ?? c.OpenMissions.OrderBy(m => m.Plan.PickupTime ?? DateTimeOffset.MaxValue).FirstOrDefault()
            ?? c.Missions.Where(m => m.Phase == MissionPhase.Complete).OrderByDescending(m => m.LandedAt).FirstOrDefault();

        var jobs = c.Missions.Where(m => m.IsOpen || m.Phase == MissionPhase.Negotiating)
            .OrderBy(m => m.Plan.PickupTime ?? DateTimeOffset.MaxValue)
            .Select(m =>
            {
                bool late = m.Phase == MissionPhase.Booked && m.Plan.PickupTime is { } due && due < now;
                return new JobRow(m.Id, m.Title, m.Plan.PickupTime is { } t ? LocalTime(t) : L.T("No time yet"),
                    L.F("{0} to {1}", m.Pickup?.Name, m.Dropoff?.Name), late ? L.T("Late") : PhaseWords(m.Phase),
                    late ? Palette.Stop : m.IsFlying ? Palette.Good : Palette.Quiet, m == focus);
            }).ToList();

        bool ringing = h.Ringing is not null;
        var talking = h.TalkingTo;
        string phoneTitle = ringing ? h.Ringing!.Person.Name
            : talking is not null && h.TalkGroup.Count > 1 ? string.Join(", ", h.TalkGroup)
            : talking?.Name ?? L.T("No calls");
        string phoneSub = ringing
            ? (h.Ringing!.Person.ReferredBy is { } r ? L.F("{0}, knows {1}", h.Ringing.Person.Job, r) : h.Ringing.Person.Job)
            : talking is not null
                ? h.TalkMode switch
                {
                    TalkMode.Phone => L.T("On the phone"),
                    TalkMode.InPerson => L.T("Face to face at the strip"),
                    TalkMode.Intercom => L.T("On the headset"),
                    _ => L.T("Saying goodbye"),
                }
                : L.T(h.ReadyForCalls ? "The phone will ring when someone needs a flight" : "Add your keys in Settings to get calls");

        var statuses = new List<StatusRow>
        {
            new(L.T("Sim"), h.SimHealth.Text, Palette.For(h.SimHealth.Level)),
            new(L.T("Mic"), h.Settings.MicMuted ? L.T("Muted (Scroll Lock)") : h.MicHealth.Text, h.Settings.MicMuted ? Palette.Caution : Palette.For(h.MicHealth.Level)),
            new("Claude", h.AiHealth.Text, Palette.For(h.AiHealth.Level)),
            new(L.T("Voices"), h.VoiceHealth.Text, Palette.For(h.VoiceHealth.Level)),
        };

        return new Picture
        {
            Company = c.CompanyName,
            Cash = Nok(c.CashNok),
            CashBrush = c.CashNok < 0 ? Palette.Stop : Palette.Text,
            Rank = c.RankName,
            Reputation = c.Reputation,
            Career = L.F("{0} flights, {1:0.0} hours", c.FlightsCompleted, c.HoursFlown),
            Statuses = statuses,
            NeedKeys = !h.ReadyForCalls,
            IsRinging = ringing,
            IsTalking = talking is not null,
            IsIdle = !ringing && talking is null,
            PhoneTitle = phoneTitle,
            PhoneSub = phoneSub,
            Partial = h.Partial,
            HasPartial = h.Partial.Length > 0,
            Chat = h.Chat.TakeLast(40).Select(l => new ChatRow(l.Who, l.Text, l.FromPilot, l.FromPilot ? Palette.Quiet : Palette.Caution)).ToList(),
            HangUpLabel = L.T(h.TalkMode == TalkMode.Phone ? "Hang up" : "Stop talking"),
            MicTestLabel = L.T(h.TestingMic ? "Stop the mic test" : "Test my mic"),
            Timing = h.LastTiming ?? "",
            Card = focus is null ? null : BuildCard(h, focus, aircraft),
            NoCard = focus is null,
            Jobs = jobs,
            NoJobs = jobs.Count == 0,
            Inbox = c.Inbox.AsEnumerable().Reverse().Take(15).Select(s => new SmsRow(Clock.ToLocal(s.When).ToString("ddd HH:mm", Culture), s.From, s.Text)).ToList(),
            People = c.People.Where(p => p.FlightsWithYou > 0 || p.Talks > 0).OrderByDescending(p => p.LastContact).Take(200).Select(p => BuildPerson(h, p)).ToList(),
            PeopleSummary = L.F("{0} people know you", c.People.Count(p => p.FlightsWithYou > 0 || p.Talks > 0)) + ". "
                + L.F("{0} trust you", c.People.Count(p => (p.FlightsWithYou > 0 || p.Talks > 0) && p.Trust > 15)) + ". "
                + L.F("{0} are wary of you", c.People.Count(p => (p.FlightsWithYou > 0 || p.Talks > 0) && p.Trust < -15)) + ".",
            NoPeople = !c.People.Any(p => p.FlightsWithYou > 0 || p.Talks > 0),
            Logbook = c.Missions.Where(m => m.Review is not null).OrderByDescending(m => m.LandedAt).Take(100).Select(m => new LogRow(
                m.LandedAt is { } l ? LocalTime(l) : "", m.Title, L.F("{0} to {1}", m.Pickup?.Name, m.Dropoff?.Name), L.F("{0:0.0} of 5", m.Review!.Stars),
                new string('★', (int)Math.Round(m.Review.Stars)) + new string('☆', 5 - (int)Math.Round(m.Review.Stars)),
                Nok(m.Review.PayNok + m.Review.TipNok - m.Review.RepairNok), m.Review.Quote ?? "", m.Review.Points.ToList(), m.Review.PayNok + m.Review.TipNok - m.Review.RepairNok < 0 ? Palette.Stop : Palette.Good)).ToList(),
            LogStats = LogStatsFor(c),
            NoFlights = !c.Missions.Any(m => m.Review is not null),
            NoTalks = c.Talks.Count == 0,
            Talks = TalksFor(c),
            Ledger = c.Ledger.AsEnumerable().Reverse().Take(100).Select(e => new LedgerRow(Clock.ToLocal(e.When).ToString("d MMM HH:mm", Culture), e.Text, Nok(e.AmountNok), e.AmountNok < 0 ? Palette.Stop : Palette.Good)).ToList(),
            FuelCache = string.Join(", ", c.FuelCache.Where(k => k.Value >= 1).Select(k => L.F("{0:0} gal at {1}", k.Value, WorldData.ById(k.Key)?.Name ?? k.Key))),
            Fuel = BuildFuel(h, focus, aircraft),
            Map = new MapData(WorldData.Places.Select(p => new MapMarker(p.Name, p.Lat, p.Lon, p.Remote, p.Id == "ENMP", p.Kind == PlaceKind.Airport, p.RunwayM, p.FuelPriceNokPerGal is not null)).ToList(),
                focus?.Pickup, focus?.Dropoff, focus?.Stops.Select(x => new MapStop(x.Name, x.Lat, x.Lon, x.Seen)).ToList() ?? [],
                WorldData.Landmarks.Where(x => !WorldData.Places.Any(p => x.Name == p.Name || x.Name == p.Name + " lodge")).Select(x => new MapName(L.T(x.Name), x.Lat, x.Lon)).ToList(),
                h.KnownPlaces.Select(x => new MapName(x.Name, x.Lat, x.Lon)).ToList(), aircraft.CruiseKtas),
            Route = focus is null ? [] : BuildRoute(focus),
        };
    }

    private static IReadOnlyList<RouteRow> BuildRoute(Mission m)
    {
        var rows = new List<RouteRow>();
        if (m.Pickup is { } a)
        {
            bool there = m.ArrivedAtPickup is not null;
            rows.Add(new RouteRow(L.F("{0} {1}", there ? "\u2713" : "1.", L.F("Pick up at {0}", a.Name)), there ? Palette.Quiet : Palette.Text));
        }
        int n = 2;
        foreach (var s in m.Stops)
            rows.Add(new RouteRow(L.F("{0} {1}", s.Seen ? "\u2713" : $"{n++}.", L.F("Fly over {0}", s.Name)), s.Seen ? Palette.Good : Palette.Caution));
        if (m.Dropoff is { } b)
            rows.Add(new RouteRow(L.F("{0} {1}", $"{n}.", L.F("Land at {0}", b.Name)), Palette.Text));
        return rows;
    }

    private static readonly IBrush[] Avatars = new[] { "#2F6F62", "#7A5C2E", "#3F5E8C", "#7B3F5C", "#556B2F", "#5A4B8C", "#2E6E7E", "#8A4B2E" }
        .Select(c => (IBrush)new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse(c))).ToArray();
    private static readonly IBrush Neutral = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#7F9C95"));

    private static IReadOnlyList<StatRow> LogStatsFor(CareerState c)
    {
        var done = c.Missions.Where(m => m.Review is not null).Select(m => m.Review!).ToList();
        return
        [
            new($"{done.Count}", L.T("flights")),
            new(done.Count > 0 ? $"{done.Average(r => r.Stars):0.0}" : "-", L.T("average stars")),
            new(Nok(done.Sum(r => r.PayNok + r.TipNok)), L.T("paid")),
            new(Nok(done.Sum(r => r.RepairNok)), L.T("for the mechanic")),
            new($"{c.Talks.Count}", L.T("conversations")),
            new($"{c.Talks.Count(t => t.Offence > 0)}", L.T("you behaved badly")),
        ];
    }

    private static int _talksBuiltFor = -1;
    private static IReadOnlyList<TalkRow> _talksBuilt = [];

    /// <summary>Only rebuilt when a conversation has been added, so a page you are reading stays still.</summary>
    private static IReadOnlyList<TalkRow> TalksFor(CareerState c)
    {
        if (c.Talks.Count == _talksBuiltFor) return _talksBuilt;
        _talksBuiltFor = c.Talks.Count;
        _talksBuilt = c.Talks.AsEnumerable().Reverse().Take(200).Select(t =>
        {
            string flag = t.Offence >= 2 ? L.T("You crossed the line") : t.Offence == 1 ? L.T("You were rude") : t.HungUp ? L.T("They hung up") : "";
            string mode = t.Mode switch { "Phone" => L.T("On the phone"), "InPerson" => L.T("At the strip"), "Intercom" => L.T("In the air"), _ => L.T("After landing") };
            var lines = t.Lines.Select(line =>
            {
                int colon = line.IndexOf(": ", StringComparison.Ordinal);
                string who = colon > 0 ? line[..colon] : "";
                return new TalkLine(who, colon > 0 ? line[(colon + 2)..] : line, who == c.PilotName ? Palette.Caution : Palette.Quiet);
            }).ToList();
            return new TalkRow(LocalTime(t.When), string.Join(", ", t.Names), t.MissionTitle is null ? mode : $"{mode}, {t.MissionTitle}", flag, t.Offence > 0 ? Palette.Stop : Palette.Caution, flag.Length > 0, t.Offence > 0, L.F("{0} lines", t.Lines.Count), lines);
        }).ToList();
        return _talksBuilt;
    }

    private static PersonRow BuildPerson(GameHost h, Game.People.Person p)
    {
        var c = h.Career;
        var mine = c.Missions.Where(m => (m.CallerId == p.Id || m.CompanionIds.Contains(p.Id)) && (p.ForgottenAt is null || (m.LandedAt ?? m.CreatedAt) > p.ForgottenAt))
            .Where(m => m.Phase is not (MissionPhase.Ringing or MissionPhase.Declined))
            .OrderByDescending(m => m.LandedAt ?? m.Plan.PickupTime ?? m.CreatedAt).ToList();
        var done = mine.Where(m => m.Review is not null).ToList();
        double paid = done.Where(m => m.CallerId == p.Id).Sum(m => m.Review!.PayNok + m.Review.TipNok);
        double? avg = done.Count > 0 ? done.Average(m => m.Review!.Stars) : null;
        string standing = L.T(p.Trust switch { > 50 => "Trusts you completely", > 15 => "Likes you", > -15 => "Neutral", > -50 => "Wary of you", _ => "Will not fly with you" });
        var standingBrush = p.Trust > 15 ? Palette.Good : p.Trust < -15 ? Palette.Stop : Palette.Caution;
        string From = p.Home.Length > 0 ? p.Home : p.Nationality;
        string first = p.Name.Split(' ')[0];
        string initials = string.Concat(p.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[0])).ToUpperInvariant();
        string Last = p.LastContact is { } t ? Clock.ToLocal(t).ToString("d. MMM HH:mm", Culture) : L.T("Never");

        BarRow Bar(string label, double value, string text, IBrush brush) => new(L.T(label), Math.Clamp(value, 0, 1), text, brush);
        string Level(double v) => v switch { > 0.75 => L.T("Very high"), > 0.5 => L.T("High"), > 0.25 => L.T("Medium"), _ => L.T("Low") };

        var bars = new List<BarRow>
        {
            Bar("Trust", (p.Trust + 100) / 200, $"{p.Trust:+0;-0;0}", standingBrush),
            Bar("Fear of flying", p.Fear, Level(p.Fear), p.Fear > 0.6 ? Palette.Stop : Palette.Caution),
            Bar("Talkative", p.Chatty, Level(p.Chatty), Neutral),
            Bar("Temper", p.Temper, Level(p.Temper), p.Temper > 0.7 ? Palette.Stop : p.Temper > 0.4 ? Palette.Caution : Palette.Good),
            Bar("Generosity", p.Wealth, Level(p.Wealth), Palette.Good),
            // Honesty stays hidden until you have flown with them. Knowing it on the phone would spoil the scale.
            p.FlightsWithYou > 0 ? Bar("Honesty", p.Honesty, Level(p.Honesty), p.Honesty < 0.4 ? Palette.Stop : Palette.Good)
                : Bar("Honesty", 0, L.T("Fly with them to find out"), Neutral),
        };

        string voiceName = h.Caster.Voices.FirstOrDefault(v => v.Id == p.VoiceId)?.Name.Split(" - ")[0] ?? L.T("Not known yet");
        var weighedOn = c.Missions.Any(m => m.Plan.BaggageWeighed && m.Plan.Passengers.Any(x => x.PersonId == p.Id));
        var facts = new List<FactRow>
        {
            new(L.T("From"), From), new(L.T("Age"), L.F("{0} years", p.Age)), new(L.T("Voice"), voiceName),
            new(L.T("Weight"), weighedOn ? L.F("{0:0} kg, weighed", p.WeightKg) : L.T("Never weighed")),
            new(L.T("Flights"), p.FlightsWithYou.ToString()), new(L.T("Talks"), p.Talks.ToString()),
            new(L.T("Paid you"), Nok(paid)), new(L.T("Average"), avg is { } a ? L.F("{0:0.0} of 5", a) : "-"),
            new(L.T("Last contact"), Last),
        };
        if (p.ReferredBy is { } by) facts.Add(new(L.T("Referred by {0}").Split(' ')[0], by));

        return new PersonRow
        {
            Id = p.Id, Name = p.Name, Initials = initials, AvatarBrush = Avatars[Math.Abs(StableHash(p.Id)) % Avatars.Length],
            Line = $"{L.T(p.Job)}, {From}", Standing = standing, StandingBrush = standingBrush, TrustValue = (p.Trust + 100) / 200, TrustRaw = p.Trust, FlightCount = p.FlightsWithYou,
            Memory = p.Memories.LastOrDefault()?.Text ?? "", HasMemory = p.Memories.Count > 0,
            QuickFacts = $"{L.T("Flights")} {p.FlightsWithYou}   {L.T("Talks")} {p.Talks}   {L.T("Paid you")} {Nok(paid)}",
            CallLabel = L.F("Call {0}", first), Personality = L.T(p.Personality),
            Bars = bars, Facts = facts,
            Memories = p.Memories.AsEnumerable().Reverse().Select(m => new MemoryRow(Clock.ToLocal(m.When).ToString("d. MMM yyyy", Culture), m.Text)).ToList(),
            NoMemories = p.Memories.Count == 0,
            Flights = mine.Select(m => new PersonFlightRow(
                (m.LandedAt ?? m.Plan.PickupTime ?? m.CreatedAt) is var w ? Clock.ToLocal(w).ToString("d. MMM", Culture) : "",
                m.Title, L.F("{0} to {1}", m.Pickup?.Name, m.Dropoff?.Name),
                m.Review is { } r ? L.F("{0:0.0} of 5", r.Stars) : PhaseWords(m.Phase),
                m.Review is { } rv ? Nok(rv.PayNok + rv.TipNok) : m.Plan.PriceNok is { } pr ? Nok(pr) : "")).ToList(),
            NoFlights = mine.Count == 0,
            Record = p.Record.AsEnumerable().Reverse().Select(e => new RecordRow(Clock.ToLocal(e.When).ToString("d. MMM HH:mm", Culture), e.Text, $"{e.TrustChange:+0;-0;0}",
                e.TrustChange < 0 ? Palette.Stop : Palette.Good)).ToList(),
            NoRecord = p.Record.Count == 0,
            HasSerious = p.Record.Any(e => e.Serious),
            SeriousLine = p.Record.LastOrDefault(e => e.Serious)?.Text ?? "",
        };
    }

    private static int StableHash(string s)
    {
        int h = 17;
        foreach (char ch in s) h = unchecked(h * 31 + ch);
        return h == int.MinValue ? 0 : h;
    }

    private static FuelPicture BuildFuel(GameHost h, Mission? m, AircraftProfile a)
    {
        var t = h.Latest;
        double onBoard = t?.FuelGal ?? 0;
        var place = h.PlaceHere();
        var advice = h.FuelNeed(m is { Phase: not (MissionPhase.Complete or MissionPhase.Cancelled or MissionPhase.Declined or MissionPhase.NoShow) } ? m : null);
        double? price = place?.FuelPriceNokPerGal;
        double cached = place is null ? 0 : h.Career.FuelCache.GetValueOrDefault(place.Id);
        bool parked = t is { OnGround: true, EngineRunning: false };

        string Label(string text, double target)
        {
            double add = Math.Max(0, target - onBoard);
            if (add < 0.5) return L.F("{0}: you have enough", text);
            return price is { } p ? L.F("{0}: +{1:0} gal, {2}", text, add, Nok(add * p)) : L.F("{0}: +{1:0} gal", text, add);
        }

        string here = t is null ? L.T("Start the sim to see your fuel")
            : t.OnGround == false ? L.T("In the air")
            : place is null ? L.T("There is no fuel out here")
            : price is { } unit ? L.F("{0} sells fuel at {1} per gal", place.Name, Nok(unit))
            : cached >= 1 ? L.F("{0:0} gal of your own in the shed at {1}", cached, place.Name)
            : L.F("No fuel at {0}. Fly some in.", place.Name);

        string need = advice is null ? L.T("No flight selected")
            : !advice.FuelAtDestination ? L.F("This flight needs {0:0} gal. There is no fuel at {1}, so {2:0} gal for there and back.", advice.OneWayGal, m!.Dropoff?.Name, advice.RoundTripGal)
            : L.F("This flight needs {0:0} gal including reserve.", advice.OneWayGal);

        string warning = advice is null ? "" : !advice.OneWayFits ? L.F("The tanks hold {0:0} gal, so this flight needs a fuel stop.", a.UsableFuelGal)
            : !advice.FuelAtDestination && !advice.RoundTripFits ? L.F("There and back does not fit in {0:0} gal. Bring fuel out first, or fuel on the way.", a.UsableFuelGal)
            : t is { OnGround: true } && !parked ? L.T("Shut down the engine to take fuel.") : "";

        return new FuelPicture
        {
            OnBoard = L.F("{0:0} gal on board", onBoard), Here = here, Need = need, Warning = warning, HasWarning = warning.Length > 0,
            HasOneWay = advice is { OneWayFits: true }, HasRoundTrip = advice is { RoundTripFits: true, FuelAtDestination: false },
            OneWayLabel = advice is null ? "" : Label(L.T("Fuel for this flight"), advice.OneWayGal),
            RoundTripLabel = advice is null ? "" : Label(L.T("Fuel for there and back"), advice.RoundTripGal),
            OneWayTarget = advice?.OneWayGal ?? 0, RoundTripTarget = advice?.RoundTripGal ?? 0,
            FullLabel = Label(L.T("Fill the tanks"), a.UsableFuelGal), FullTarget = a.UsableFuelGal,
            CanFuel = parked && place is not null && (price is not null || cached >= 1),
        };
    }

    private static string PhaseWords(MissionPhase p) => p switch
    {
        MissionPhase.Ringing => L.T("Ringing"),
        MissionPhase.Negotiating => L.T("Talking it over"),
        MissionPhase.Booked => L.T("Booked"),
        MissionPhase.AtPickup => L.T("At the pickup"),
        MissionPhase.Boarding => L.T("Boarding"),
        MissionPhase.Loaded => L.T("Loaded"),
        MissionPhase.Airborne => L.T("In the air"),
        MissionPhase.Landed => L.T("Landed"),
        MissionPhase.Complete => L.T("Finished"),
        MissionPhase.Declined => L.T("Declined"),
        MissionPhase.Cancelled => L.T("Cancelled"),
        MissionPhase.NoShow => L.T("You never came"),
        _ => p.ToString(),
    };

    private static CardPicture BuildCard(GameHost h, Mission m, AircraftProfile a)
    {
        var t = h.Latest;
        var forecast = h.ForecastFor(m);
        double headwind = 0;
        if (forecast?.WindKt is { } wind && forecast.WindFromDeg is { } dir && m.Pickup is { } p1 && m.Dropoff is { } p2)
            headwind = Geo.HeadwindKt(Geo.BearingDeg(p1.Lat, p1.Lon, p2.Lat, p2.Lon), dir, wind);

        // After loading, the fuel on board is the fuel. Before that, the card shows what the trip needs.
        bool loaded = m.Phase is MissionPhase.Loaded or MissionPhase.Airborne or MissionPhase.Landed;
        double? fuelNow = (m.Phase is MissionPhase.AtPickup or MissionPhase.Boarding || loaded) ? t?.FuelGal : null;
        double pilotKg = h.PilotKgNow();
        var (load, fuel, nm) = PlanEvaluator.Evaluate(m, a, pilotKg, headwind, forecast?.TempC ?? t?.OatC ?? 5, fuelNow);

        var seats = new List<SeatRow>();
        var pax = m.Plan.Passengers.OrderByDescending(x => x.Kg).ToList();
        seats.Add(new SeatRow(L.T("Front left"), h.Career.PilotName, $"{pilotKg:0} kg", false));
        string[] labels = [L.T("Front right"), L.T("Rear left"), L.T("Rear right")];
        for (int i = 0; i < labels.Length; i++)
        {
            if (i < pax.Count) seats.Add(new SeatRow(labels[i], pax[i].Name, $"{pax[i].Kg:0} kg", !pax[i].KgKnown));
            else seats.Add(new SeatRow(labels[i], L.T("Empty"), "", false));
        }
        for (int i = labels.Length; i < pax.Count; i++) seats.Add(new SeatRow(L.T("No seat"), pax[i].Name, $"{pax[i].Kg:0} kg", !pax[i].KgKnown));
        double bags = m.Plan.BaggageKg + m.Plan.CargoKg;
        string bagName = m.Plan.BaggageWeighed ? L.T("Weighed on the scale") : m.Plan.CargoKg > 0 ? (m.Plan.CargoNote.Length > 0 ? m.Plan.CargoNote : L.T("Cargo")) : L.T(m.Plan.BaggageKg > 0 ? "What they told you" : "Nothing said yet");
        seats.Add(new SeatRow(L.T("Baggage"), bagName, $"{bags:0} kg", !m.Plan.BaggageWeighed && bags > 0));

        double room = Units.LbToKg(load.ExtraPayloadRoomLb);
        var findings = load.Findings.OrderByDescending(f => f.Level).Select(f => new FindingRow(f.Text,
            f.Level == FindingLevel.Stop ? Palette.Stop : f.Level == FindingLevel.Caution ? Palette.Caution : Palette.Quiet)).ToList();
        if (t is not null && loaded && t.TotalWeightLb > a.MaxGrossLb)
            findings.Insert(0, new FindingRow(L.F("The sim says {0:0} kg over maximum weight right now", Units.LbToKg(t.TotalWeightLb - a.MaxGrossLb)), Palette.Stop));

        string weather = forecast is null ? L.T("No weather data yet") : WeatherAdvice.Judge(forecast, m.Pickup!, m.Plan.PickupTime ?? DateTimeOffset.Now).Text;
        var metar = m.Pickup is { } pk ? h.Weather.Metar(pk.MetarStation) : null;
        if (metar is not null) weather += ". " + L.F("{0} now: {1}", m.Pickup!.MetarStation, metar.Summary());

        var fromStrip = m.Pickup; var toStrip = m.Dropoff;
        string strip = "";
        if (fromStrip is not null) strip = L.F("Takeoff {0:0} m of {1:0} m at {2}", load.TakeoffDistanceM, fromStrip.RunwayM, fromStrip.Name);
        if (toStrip is not null) strip += L.F(", landing {0:0} m of {1:0} m at {2}", load.LandingDistanceM, toStrip.RunwayM, toStrip.Name);

        var r = m.Review;
        bool canTalkAtStrip = m.Phase is MissionPhase.AtPickup or MissionPhase.Boarding;
        bool peopleThere = m.Plan.Passengers.Count > 0 || m.Truth.Passengers.Count > 0;
        string meet = !peopleThere || m.Pickup is null || m.Phase is not (MissionPhase.Negotiating or MissionPhase.Booked or MissionPhase.AtPickup or MissionPhase.Boarding) ? ""
            : m.Phase is MissionPhase.AtPickup or MissionPhase.Boarding && m.MeetLat is not null ? L.T("They are at your plane. Shut down and talk to them.")
            : m.Pickup.Kind == PlaceKind.Airport ? L.F("Park anywhere at {0}. They come to your plane.", m.Pickup.Name)
            : L.F("They are waiting by the strip at {0}.", m.Pickup.Name);
        return new CardPicture
        {
            Id = m.Id, Title = m.Title,
            Route = L.F("{0} to {1}", m.Pickup?.Name ?? L.T("Unknown"), m.Dropoff?.Name ?? L.T("Unknown")),
            When = m.Plan.PickupTime is { } when ? LocalTime(when) : L.T("No time agreed yet"),
            Price = m.Plan.PricePerPersonNok is { } each ? L.F("{0} per person, {1} in all", Nok(each), Nok(m.Plan.PriceNok ?? each))
                : m.Plan.PriceNok is { } price ? L.F("{0} for the whole trip", Nok(price)) : L.F("Around {0} is normal", Nok(h.SuggestedPrice(m))),
            Phase = PhaseWords(m.Phase),
            DistanceLine = nm > 0 ? L.F("{0:0} nm, {1:0} minutes", nm, fuel.TripHours * 60) + (headwind > 5 ? L.F(", {0:0} kt headwind", headwind) : headwind < -5 ? L.F(", {0:0} kt tailwind", -headwind) : "") : L.T("Local flight"),
            MeetLine = meet, Weather = weather, Seats = seats,
            TakeoffWeight = L.F("{0:0} kg of {1:0} kg", Units.LbToKg(load.TakeoffLb), Units.LbToKg(load.MaxGrossLb)),
            Room = room >= 0 ? L.F("{0:0} kg to spare", room) : L.F("{0:0} kg too heavy", -room),
            RoomBrush = room < 0 ? Palette.Stop : room < 25 ? Palette.Caution : Palette.Good,
            FuelLine = L.F("{0:0} gal on board, trip needs {1:0} gal with reserve", load.FuelGal, fuel.RequiredGal),
            StripLine = strip,
            WeightFraction = Math.Clamp(load.TakeoffLb / load.MaxGrossLb, 0, 1.1) / 1.1,
            FuelFraction = Math.Clamp(load.FuelGal / a.UsableFuelGal, 0, 1),
            FuelRequiredFraction = Math.Clamp(fuel.RequiredGal / a.UsableFuelGal, 0, 1),
            Findings = findings,
            Changes = m.Plan.Changes.TakeLast(4).Reverse().Select(c => new ChangeRow(c.From.Length > 0 ? L.F("{0}: {1} became {2}", L.T(c.Field), c.From, c.To) : $"{L.T(c.Field)}: {c.To}")).ToList(),
            Notes = string.Join(" ", m.Plan.Notes),
            HasChanges = m.Plan.Changes.Count > 0,
            Envelope = new EnvelopeData(a, load.TakeoffLb, load.TakeoffCgIn, load.LandingLb, load.LandingCgIn, loaded ? t?.TotalWeightLb : null,
                loaded && t is not null ? null : null, load.IsLegal),
            CanCall = m.Phase is MissionPhase.Booked or MissionPhase.AtPickup && h.TalkingTo is null,
            CanMeet = canTalkAtStrip && h.TalkingTo is null && peopleThere,
            CanWeigh = canTalkAtStrip && !m.Plan.BaggageWeighed,
            CanBoard = canTalkAtStrip,
            CanDeplane = m.Phase == MissionPhase.Landed && t is { OnGround: true, GroundSpeedKt: < 1 },
            CanCancel = Cancellation.Allowed(m),
            CanMoveTime = m.Phase == MissionPhase.Booked,
            PickupAt = m.Plan.PickupTime,
            BoardLabel = L.T(m.Plan.Passengers.Count > 0 ? "Board everyone" : "Load the cargo"),
            HasReview = r is not null,
            ReviewLine = r is null ? "" : L.F("{0:0.0} of 5, paid {1}", r.Stars, Nok(r.PayNok + r.TipNok)) + (r.RepairNok > 0 ? L.F(", mechanic {0}", Nok(r.RepairNok)) : "") + L.F(r.ReputationDelta >= 0 ? ", reputation up {0:0.0}" : ", reputation down {0:0.0}", Math.Abs(r.ReputationDelta)),
            ReviewPoints = r is null ? "" : string.Join(", ", r.Points),
            ReviewQuote = r?.Quote ?? "",
        };
    }
}
