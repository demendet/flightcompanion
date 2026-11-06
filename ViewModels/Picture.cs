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
public sealed record PersonRow(string Name, string Line, string Standing, string Memory, IBrush StandingBrush);
public sealed record LogRow(string When, string Title, string Route, string Stars, string Money, string Quote, string Points);
public sealed record LedgerRow(string When, string Text, string Amount, IBrush AmountBrush);
public sealed record SeatRow(string Label, string Who, string Kg, bool Guess);
public sealed record FindingRow(string Text, IBrush Brush);
public sealed record ChangeRow(string Text);

public sealed record EnvelopeData(AircraftProfile Profile, double TakeoffLb, double TakeoffCg, double LandingLb, double LandingCg, double? LiveLb, double? LiveCg, bool Legal);

public sealed record MapMarker(string Name, double Lat, double Lon, bool Remote, bool LabelLeft);
public sealed record MapData(IReadOnlyList<MapMarker> Places, Place? From, Place? To, double? PlaneLat, double? PlaneLon, double PlaneHeading);

public sealed record CardPicture
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Route { get; init; }
    public required string When { get; init; }
    public required string Price { get; init; }
    public required string Phase { get; init; }
    public required string DistanceLine { get; init; }
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
    public required IReadOnlyList<LogRow> Logbook { get; init; }
    public required IReadOnlyList<LedgerRow> Ledger { get; init; }
    public required MapData Map { get; init; }
    public required string FuelCache { get; init; }
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
        string phoneTitle = ringing ? h.Ringing!.Person.Name : talking?.Name ?? L.T("No calls");
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
            People = c.People.Where(p => p.FlightsWithYou > 0 || p.ArcId is not null).OrderByDescending(p => p.LastContact).Take(60).Select(p => new PersonRow(
                p.Name, L.F("{0}, {1}, flown {2} times", p.Job, p.Nationality, p.FlightsWithYou),
                L.T(p.Trust switch { > 50 => "Trusts you completely", > 15 => "Likes you", > -15 => "Neutral", > -50 => "Wary of you", _ => "Will not fly with you" }),
                p.Memories.LastOrDefault()?.Text ?? "", p.Trust > 15 ? Palette.Good : p.Trust < -15 ? Palette.Stop : Palette.Quiet)).ToList(),
            Logbook = c.Missions.Where(m => m.Review is not null).OrderByDescending(m => m.LandedAt).Take(100).Select(m => new LogRow(
                m.LandedAt is { } l ? LocalTime(l) : "", m.Title, L.F("{0} to {1}", m.Pickup?.Name, m.Dropoff?.Name), L.F("{0:0.0} of 5", m.Review!.Stars),
                Nok(m.Review.PayNok + m.Review.TipNok - m.Review.RepairNok), m.Review.Quote ?? "", string.Join(", ", m.Review.Points))).ToList(),
            Ledger = c.Ledger.AsEnumerable().Reverse().Take(100).Select(e => new LedgerRow(Clock.ToLocal(e.When).ToString("d MMM HH:mm", Culture), e.Text, Nok(e.AmountNok), e.AmountNok < 0 ? Palette.Stop : Palette.Good)).ToList(),
            FuelCache = string.Join(", ", c.FuelCache.Where(k => k.Value >= 1).Select(k => L.F("{0:0} gal at {1}", k.Value, WorldData.ById(k.Key)?.Name ?? k.Key))),
            Map = new MapData(WorldData.Places.Select(p => new MapMarker(p.Name, p.Lat, p.Lon, p.Remote, p.Id == "ENMP")).ToList(), focus?.Pickup, focus?.Dropoff, h.Latest?.Lat, h.Latest?.Lon, h.Latest?.HeadingTrue ?? 0),
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
        return new CardPicture
        {
            Id = m.Id, Title = m.Title,
            Route = L.F("{0} to {1}", m.Pickup?.Name ?? L.T("Unknown"), m.Dropoff?.Name ?? L.T("Unknown")),
            When = m.Plan.PickupTime is { } when ? LocalTime(when) : L.T("No time agreed yet"),
            Price = m.Plan.PriceNok is { } price ? Nok(price) : L.F("Around {0} is normal", Nok(h.SuggestedPrice(m))),
            Phase = PhaseWords(m.Phase),
            DistanceLine = nm > 0 ? L.F("{0:0} nm, {1:0} minutes", nm, fuel.TripHours * 60) + (headwind > 5 ? L.F(", {0:0} kt headwind", headwind) : headwind < -5 ? L.F(", {0:0} kt tailwind", -headwind) : "") : L.T("Local flight"),
            Weather = weather, Seats = seats,
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
            BoardLabel = L.T(m.Plan.Passengers.Count > 0 ? "Board everyone" : "Load the cargo"),
            HasReview = r is not null,
            ReviewLine = r is null ? "" : L.F("{0:0.0} of 5, paid {1}", r.Stars, Nok(r.PayNok + r.TipNok)) + (r.RepairNok > 0 ? L.F(", mechanic {0}", Nok(r.RepairNok)) : "") + L.F(r.ReputationDelta >= 0 ? ", reputation up {0:0.0}" : ", reputation down {0:0.0}", Math.Abs(r.ReputationDelta)),
            ReviewPoints = r is null ? "" : string.Join(", ", r.Points),
            ReviewQuote = r?.Quote ?? "",
        };
    }
}
