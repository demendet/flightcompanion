using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.Career;
using FlightCompanion.Game.Flight;
using FlightCompanion.Game.Missions;
using FlightCompanion.Game.People;
using FlightCompanion.Game.Talk;
using FlightCompanion.Game.World;
using Xunit;

namespace FlightCompanion.Tests;

public class LoadTests
{
    private static LoadResult Compute(IReadOnlyList<double> pax, double bagKg, double fuelGal, double trip = 8, RunwayInfo? dep = null)
        => LoadPlanner.Compute(new LoadRequest
        {
            Aircraft = AircraftProfile.C172, PilotKg = 85, PassengerKg = pax, BaggageKg = bagKg,
            FuelGal = fuelGal, TripFuelGal = trip, Departure = dep,
        });

    [Fact]
    public void Takeoff_weight_and_cg_match_a_hand_calculation()
    {
        // Empty 1680 lb at 41 in = 68880. Pilot 85 kg = 187.39 lb at 37. Front pax 90 kg = 198.42 lb at 37.
        // Rear pax 70 kg = 154.32 lb at 73. Baggage 20 kg = 44.09 lb at 95. Fuel 30 gal = 180 lb at 48.
        var r = Compute([70, 90], 20, 30);
        double weight = 1680 + 187.39 + 198.42 + 154.32 + 44.09 + 180;
        double moment = 68880 + 187.39 * 37 + 198.42 * 37 + 154.32 * 73 + 44.09 * 95 + 180 * 48;
        Assert.Equal(weight, r.TakeoffLb, 0);
        Assert.Equal(moment / weight, r.TakeoffCgIn, 1);
    }

    [Fact]
    public void Heaviest_passenger_sits_up_front()
    {
        var r = Compute([60, 110], 0, 20);
        var front = r.Stations.Single(s => s.Role == StationRole.FrontSeat);
        Assert.Equal(Units.KgToLb(110), front.Lb, 1);
    }

    [Fact]
    public void Four_big_people_with_full_fuel_is_overweight_and_flagged()
    {
        var r = Compute([95, 95, 95], 30, 53);
        Assert.True(r.OverGrossLb > 0);
        Assert.Contains(r.Findings, f => f.Code == "gross");
    }

    [Fact]
    public void Baggage_beyond_the_placard_has_nowhere_to_go()
    {
        var r = Compute([80], 90, 20);   // 198 lb against a 120 lb combined limit
        Assert.True(r.OverflowBaggageLb > 70);
        Assert.False(r.IsLegal);
    }

    [Fact]
    public void Baggage_fills_forward_area_first_then_rear()
    {
        var r = Compute([80], 54, 20);   // 119 lb: 120 in A would take all of it
        Assert.Equal(119, r.Stations.Single(s => s.Role == StationRole.BaggageA).Lb, 0);
        Assert.Equal(0, r.Stations.Single(s => s.Role == StationRole.BaggageB).Lb, 0);
    }

    [Fact]
    public void Heavy_load_on_a_short_soft_strip_is_refused_while_light_load_is_fine()
    {
        var strip = new RunwayInfo(400, 1200, true);
        Assert.Contains(Compute([95, 90, 85], 40, 40, dep: strip).Findings, f => f.Code == "todist");
        Assert.DoesNotContain(Compute([], 0, 15, dep: new RunwayInfo(800, 400, false)).Findings, f => f.Code == "todist");
    }

    [Fact]
    public void Forward_limit_is_linear_between_book_points()
    {
        Assert.Equal(35.0, AircraftProfile.C172.ForwardLimitAt(1800), 3);
        Assert.Equal(38.0, AircraftProfile.C172.ForwardLimitAt(2250), 3);
        Assert.Equal(41.0, AircraftProfile.C172.ForwardLimitAt(2550), 3);
    }

    [Fact]
    public void Night_needs_more_reserve_than_day()
    {
        var day = FuelPlanner.Plan(AircraftProfile.C172, 100);
        var night = FuelPlanner.Plan(AircraftProfile.C172, 100, night: true);
        Assert.Equal(AircraftProfile.C172.BurnGph * 0.25, night.ReserveGal - day.ReserveGal, 3);
        Assert.True(FuelPlanner.Plan(AircraftProfile.C172, 100, headwindKt: 30).TripGal > day.TripGal);
    }
}

public class PlanPatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Patch_changes_only_what_was_said_and_logs_each_change()
    {
        var plan = new MissionPlan { BaggageKg = 20, PickupId = "ENNA" };
        var changes = PlanPatcher.Apply(plan, new PlanPatch { BaggageTotalKg = 45, PassengerCount = 2 }, Now);
        Assert.Equal(45, plan.BaggageKg);
        Assert.Equal(2, plan.Passengers.Count);
        Assert.Equal("ENNA", plan.PickupId);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void A_repeated_patch_is_not_a_change()
    {
        var plan = new MissionPlan();
        var p = new PlanPatch { BaggageTotalKg = 45 };
        PlanPatcher.Apply(plan, p, Now);
        Assert.Empty(PlanPatcher.Apply(plan, p, Now));
    }

    [Fact]
    public void Scale_reading_beats_later_claims()
    {
        var plan = new MissionPlan { BaggageKg = 38, BaggageWeighed = true };
        PlanPatcher.Apply(plan, new PlanPatch { BaggageTotalKg = 20 }, Now);
        Assert.Equal(38, plan.BaggageKg);
    }

    [Theory]
    [InlineData("banak", "ENNA")]
    [InlineData("Lakselv Banak", "ENNA")]
    [InlineData("the lodge at Mollisjok", "ENMP")]
    [InlineData("Maggie Beach", "ENMB")]
    [InlineData("Honningsvåg", "ENHV")]
    [InlineData("the north cape", "ENHV")]
    public void Spoken_places_resolve(string spoken, string id) => Assert.Equal(id, WorldData.Find(spoken)?.Id);

    [Fact]
    public void Unknown_place_is_ignored_not_guessed()
    {
        var plan = new MissionPlan { PickupId = "ENNA" };
        PlanPatcher.Apply(plan, new PlanPatch { Pickup = "some lake I saw once" }, Now);
        Assert.Equal("ENNA", plan.PickupId);
    }

    [Fact]
    public void Local_time_is_read_in_norwegian_winter_time()
    {
        var t = PlanPatcher.ParseLocal("2026-11-07T14:00:00", null);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 13, 0, 0, TimeSpan.Zero), t!.Value.ToUniversalTime());
    }
}

public class MonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static Telemetry Sample(double s, bool ground, double agl = 0, double vs = 0, double bank = 0, double g = 1, bool stall = false, double td = 0)
        => new()
        {
            Time = T0.AddSeconds(s), OnGround = ground, AglFt = agl, VerticalFpm = vs, BankDeg = bank, GForce = g,
            StallWarning = stall, GroundSpeedKt = ground ? 0 : 90, EngineRunning = true, FuelGal = 30, TouchdownFpm = td,
        };

    private static (FlightMonitor mon, List<FlightEvent> ev) Fly()
    {
        var mon = new FlightMonitor();
        var ev = new List<FlightEvent>();
        mon.Process(Sample(0, true));
        for (int i = 1; i <= 5; i++) ev.AddRange(mon.Process(Sample(i, false, 50 * i, 500)));
        return (mon, ev);
    }

    [Fact]
    public void Takeoff_fires_once()
    {
        var (_, ev) = Fly();
        Assert.Single(ev, e => e.Kind == FlightEventKind.Takeoff);
    }

    [Fact]
    public void Steep_turn_is_one_event_not_a_stream()
    {
        var (mon, _) = Fly();
        var ev = new List<FlightEvent>();
        for (int i = 6; i <= 15; i++) ev.AddRange(mon.Process(Sample(i, false, 500, 0, 45)));
        Assert.Single(ev, e => e.Kind == FlightEventKind.SteepBank);
    }

    [Theory]
    [InlineData(60, LandingGrade.Butter)]
    [InlineData(180, LandingGrade.Good)]
    [InlineData(350, LandingGrade.Firm)]
    [InlineData(600, LandingGrade.Hard)]
    public void Landing_is_graded_from_touchdown_rate(double fpm, LandingGrade expect)
    {
        var (mon, _) = Fly();
        mon.Process(Sample(6, false, 20, -fpm));
        var ev = new List<FlightEvent>();
        ev.AddRange(mon.Process(Sample(7, true, 0, 0, td: -fpm)));
        for (int i = 8; i <= 12; i++) ev.AddRange(mon.Process(Sample(i, true)));
        var landing = Assert.Single(ev, e => e.Kind == FlightEventKind.Landing);
        Assert.Equal(fpm, landing.Value, 0);
        Assert.Equal(expect, LandingScale.Grade(landing.Value, 0));
    }

    [Fact]
    public void Bounce_is_one_landing_counted_as_a_bounce()
    {
        var (mon, _) = Fly();
        var ev = new List<FlightEvent>();
        ev.AddRange(mon.Process(Sample(6, true, 0, 0, td: -300)));
        ev.AddRange(mon.Process(Sample(7, false, 3, 100)));
        ev.AddRange(mon.Process(Sample(8, true, 0, 0, td: -150)));
        for (int i = 9; i <= 14; i++) ev.AddRange(mon.Process(Sample(i, true)));
        Assert.Single(ev, e => e.Kind == FlightEventKind.Landing);
        Assert.Equal(1, mon.LastBounces);
        Assert.Equal(300, mon.LastTouchdownFpm, 0);
    }

    [Fact]
    public void Frightening_flying_lowers_comfort_and_gentle_flying_recovers_it()
    {
        var (mon, _) = Fly();
        mon.Process(Sample(6, false, 500, 0, 65));
        double scared = mon.Comfort;
        Assert.True(scared < 100);
        for (int i = 7; i < 200; i++) mon.Process(Sample(i, false, 500));
        Assert.True(mon.Comfort > scared);
    }
}

public class CareerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Save_round_trips_and_survives_a_broken_file()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fc-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "career.json");
        var c = new CareerState { CompanyName = "Test Air", Reputation = 42 };
        c.People.Add(new Person { Name = "Klaus", Trust = 12 });
        CareerStore.Save(c, path);
        CareerStore.Save(c, path);
        Assert.Equal("Test Air", CareerStore.Load(path).CompanyName);
        Assert.Equal("Klaus", CareerStore.Load(path).People[0].Name);
        File.WriteAllText(path, "{ not json");
        Assert.Equal(42, CareerStore.Load(path).Reputation);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Honest_person_gives_no_surprises_and_a_liar_often_does()
    {
        int liars = 0, honest = 0;
        for (int i = 0; i < 300; i++)
        {
            var rng = new Random(i);
            var c = new CareerState();
            var liar = new Person { Name = "L", Honesty = 0, WeightKg = 80 };
            var truthful = new Person { Name = "H", Honesty = 1, Trust = 100, WeightKg = 80 };
            c.People.AddRange([liar, truthful]);
            var tpl = MissionCatalog.ById("fishing_camp")!;
            if (MissionFactory.Create(tpl, new CareerView(c), rng, Now, liar).Twists.Contains("heavy_bags")) liars++;
            if (MissionFactory.Create(tpl, new CareerView(c), rng, Now, truthful).Twists.Contains("heavy_bags")) honest++;
        }
        Assert.True(liars > 150, $"liars {liars}");
        Assert.True(honest < 40, $"honest {honest}");
    }

    [Fact]
    public void Heavy_bags_twist_makes_truth_exceed_the_claim()
    {
        var rng = new Random(1);
        var m = new Mission { Truth = new MissionTruth { BaggageKg = 20, ClaimedBaggageKg = 20 } };
        TwistCatalog.Apply("heavy_bags", m, rng);
        Assert.True(m.Truth.BaggageKg > m.Truth.ClaimedBaggageKg + 5);
        Assert.Single(m.SecretsForCaller);
    }

    [Fact]
    public void Return_leg_carries_the_catch_in_the_truth_but_not_the_plan()
    {
        var o = new Mission { Plan = new MissionPlan { PickupId = "ENNA", DropoffId = "ENMP", BaggageKg = 30, PriceNok = 5000 } };
        o.Truth.ReturnExtraKg = 30;
        var r = MissionFactory.CreateReturn(o, Now.AddDays(2), Now);
        Assert.Equal("ENMP", r.Plan.PickupId);
        Assert.Equal("ENNA", r.Plan.DropoffId);
        Assert.Equal(30, r.Plan.BaggageKg);
        Assert.Equal(60, r.Truth.BaggageKg);
    }

    [Fact]
    public void Director_stays_silent_during_a_call_and_only_warns_when_you_forget_someone()
    {
        var c = new CareerState();
        var d = new Director(c, new Random(3));
        Assert.Null(d.Tick(new DirectorContext { Now = Now, InCall = true }));

        var caller = new Person { Name = "Anke" };
        c.People.Add(caller);
        var m = new Mission { CallerId = caller.Id, Phase = MissionPhase.Booked, Plan = new MissionPlan { PickupId = "ENMP", PickupTime = Now.AddMinutes(-30) } };
        c.Missions.Add(m);
        var a = d.Tick(new DirectorContext { Now = Now });
        var ring = Assert.IsType<RingAbout>(a);
        Assert.Same(caller, ring.Person);
        Assert.True(m.LateCallMade);
    }

    [Fact]
    public void Abandoned_customer_becomes_a_no_show_with_consequences()
    {
        var c = new CareerState { Reputation = 50 };
        var d = new Director(c, new Random(3));
        var caller = new Person { Name = "Anke", Trust = 10 };
        c.People.Add(caller);
        var m = new Mission { CallerId = caller.Id, Phase = MissionPhase.Booked, Plan = new MissionPlan { PickupId = "ENMP", PickupTime = Now.AddHours(-3) } };
        c.Missions.Add(m);
        Assert.IsType<TextMessage>(d.Tick(new DirectorContext { Now = Now }));
        Assert.Equal(MissionPhase.NoShow, m.Phase);
        Assert.True(c.Reputation < 50);
        Assert.True(caller.Trust < 10);
        Assert.NotEmpty(caller.Memories);
    }

    [Fact]
    public void New_jobs_do_not_ring_while_passengers_are_aboard_and_they_arrive_otherwise()
    {
        var c = new CareerState { NextCallAt = Now.AddMinutes(-1) };
        var d = new Director(c, new Random(5));
        Assert.Null(d.Tick(new DirectorContext { Now = Now, PassengersAboard = true }));
        c.NextCallAt = Now.AddMinutes(-1);
        Assert.IsType<RingNewJob>(d.Tick(new DirectorContext { Now = Now }));
        Assert.Single(c.Missions);
    }

    [Fact]
    public void Good_flight_pays_tip_and_builds_standing_while_a_hard_one_costs_money()
    {
        Mission Flew(double fpm, double comfort) => new()
        {
            Plan = new MissionPlan { PriceNok = 10000, PickupId = "ENNA", DropoffId = "ENMP" },
            Score = new FlightScore { TouchdownFpm = fpm, ComfortAvgSum = comfort, ComfortSamples = 1, ComfortMin = comfort, LandedAtDestination = true },
        };
        var c = new CareerState();
        var rich = new Person { Wealth = 1 };
        var good = ReviewMaker.Make(Flew(70, 100), c, rich, Now);
        var bad = ReviewMaker.Make(Flew(650, 45), c, rich, Now);
        Assert.Equal(5, good.Stars);
        Assert.True(good.TipNok > 0);
        Assert.True(good.ReputationDelta > 0);
        Assert.True(bad.RepairNok > 0);
        Assert.True(bad.ReputationDelta < 0);
        Assert.True(bad.Stars < good.Stars);
    }

    [Fact]
    public void Every_template_can_be_generated_in_every_month_it_allows()
    {
        foreach (var t in MissionCatalog.Templates)
        {
            var c = new CareerState { Reputation = 100 };
            var m = MissionFactory.Create(t, new CareerView(c), new Random(t.Id.GetHashCode()), Now);
            Assert.NotNull(m.Pickup);
            Assert.NotNull(m.Dropoff);
            Assert.DoesNotContain("{", m.Premise);
            var (load, fuel, _) = PlanEvaluator.Evaluate(m, AircraftProfile.C172, 85);
            Assert.True(load.TakeoffLb > 1680);
            Assert.True(fuel.RequiredGal > 0);
        }
    }

    [Fact]
    public void Speaker_prompt_contains_memories_and_secrets_and_scribe_never_sees_secrets()
    {
        var c = new CareerState();
        var p = new Person { Name = "Klaus", FlightsWithYou = 2, Trust = 30 };
        p.Memories.Add(new MemoryNote(Now, "You were scared on approach."));
        var m = new Mission { Plan = new MissionPlan { PickupId = "ENNA", DropoffId = "ENMP" } };
        m.SecretsForCaller.Add("Your bags really weigh 40 kg");
        string prompt = Prompts.Speaker(p, m, c, TalkMode.Phone, null);
        Assert.Contains("scared on approach", prompt);
        Assert.Contains("really weigh 40", prompt);
        Assert.DoesNotContain("really weigh", Prompts.ScribeUser(Now, m.Plan, ["Pilot: hi"]));
    }

    [Fact]
    public void Polar_night_is_dark_at_midday_in_late_december_and_june_midnight_is_light()
    {
        Assert.True(Sun.IsDark(new DateTimeOffset(2026, 12, 21, 3, 0, 0, TimeSpan.Zero), 69.5, 24.4));
        Assert.False(Sun.IsDark(new DateTimeOffset(2026, 6, 21, 22, 0, 0, TimeSpan.Zero), 69.5, 24.4));
    }
}

public class FavoriteCallerTests
{
    private static int HowOften(Action<CareerState, Person>? setup = null)
    {
        int count = 0;
        var tpl = MissionCatalog.ById("lodge_weekend")!;
        for (int i = 0; i < 1000; i++)
        {
            var c = new CareerState();
            var fav = new Person { Name = "Odd Nilsen", Language = "no" };
            c.People.Add(fav);
            c.FavoriteCallerId = fav.Id;
            setup?.Invoke(c, fav);
            if (new CareerView(c).PickCaller(tpl, new Random(i)) == fav) count++;
        }
        return count;
    }

    [Fact]
    public void The_clone_calls_a_bit_more_often_but_not_all_the_time() => Assert.InRange(HowOften(), 130, 230);

    [Fact]
    public void Not_while_already_booked()
        => Assert.Equal(0, HowOften((c, fav) => c.Missions.Add(new Mission { CallerId = fav.Id, Phase = MissionPhase.Booked })));

    [Fact]
    public void Not_after_falling_out_with_you() => Assert.Equal(0, HowOften((_, fav) => fav.Trust = -60));
}

public class CancelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(1));

    private static (CareerState, Mission, Person) Booked(double hoursAhead, MissionPhase phase = MissionPhase.Booked)
    {
        var c = new CareerState { Reputation = 50 };
        var p = new Person { Name = "Kari", Trust = 20 };
        c.People.Add(p);
        var m = new Mission { CallerId = p.Id, Phase = phase, Plan = new MissionPlan { PickupId = "ENNA", DropoffId = "ENMP", PickupTime = Now.AddHours(hoursAhead) } };
        c.Missions.Add(m);
        return (c, m, p);
    }

    [Fact]
    public void Cancelling_days_ahead_costs_little_and_they_are_told()
    {
        var (c, m, p) = Booked(72);
        Cancellation.Apply(m, c, Now);
        Assert.Equal(MissionPhase.Cancelled, m.Phase);
        Assert.Equal(50, c.Reputation);
        Assert.Equal(18, p.Trust);
        Assert.Single(c.Inbox);
        Assert.Single(p.Memories);
    }

    [Fact]
    public void Cancelling_when_they_are_already_waiting_costs_much_more()
    {
        var (c, m, p) = Booked(-0.5, MissionPhase.AtPickup);
        Cancellation.Apply(m, c, Now);
        Assert.Equal(48, c.Reputation);
        Assert.Equal(0, p.Trust);
    }

    [Theory]
    [InlineData(MissionPhase.Booked, true)]
    [InlineData(MissionPhase.AtPickup, true)]
    [InlineData(MissionPhase.Boarding, true)]
    [InlineData(MissionPhase.Loaded, false)]
    [InlineData(MissionPhase.Airborne, false)]
    [InlineData(MissionPhase.Complete, false)]
    public void You_cannot_cancel_once_they_are_on_board(MissionPhase phase, bool allowed)
        => Assert.Equal(allowed, Cancellation.Allowed(new Mission { Phase = phase }));
}

public class SightTests
{
    private const double Lat = 69.5, Lon = 24.5;

    [Theory]
    [InlineData(0, 90, "on the right")]
    [InlineData(0, 270, "on the left")]
    [InlineData(0, 5, "straight ahead")]
    [InlineData(0, 180, "behind you")]
    [InlineData(350, 80, "on the right")]
    [InlineData(90, 0, "on the left")]
    [InlineData(180, 90, "on the left")]
    public void Left_and_right_depend_on_where_the_aircraft_points(double heading, double bearing, string side)
        => Assert.Equal(side, SightWatcher.SideOf(heading, bearing));

    private static PointOfInterest At(string name, double bearing, double km, double range = 10)
    {
        var (la, lo) = Geo.Offset(Lat, Lon, bearing, km * 1000);
        return new PointOfInterest(name, la, lo, "a lake", range);
    }

    [Fact]
    public void A_lake_on_the_left_is_described_on_the_left_and_flying_the_other_way_flips_it()
    {
        var lake = At("Vannet", 270, 3);
        Assert.Contains("on the left", SightWatcher.Visible(Lat, Lon, 0, [lake])[0].Describe());
        Assert.Contains("on the right", SightWatcher.Visible(Lat, Lon, 180, [lake])[0].Describe());
    }

    [Fact]
    public void Things_out_of_range_or_behind_are_not_mentioned()
    {
        Assert.Empty(SightWatcher.Visible(Lat, Lon, 0, [At("Far", 0, 30)]));
        Assert.Empty(SightWatcher.Visible(Lat, Lon, 0, [At("Behind", 180, 5)]));
    }

    [Fact]
    public void Each_sight_gets_one_comment_per_flight()
    {
        var w = new SightWatcher();
        var sights = new[] { At("A", 90, 2), At("B", 270, 4) };
        Assert.Equal("A", w.NextToMention(Lat, Lon, 0, sights)!.Sight.Name);
        Assert.Equal("B", w.NextToMention(Lat, Lon, 0, sights)!.Sight.Name);
        Assert.Null(w.NextToMention(Lat, Lon, 0, sights));
        w.NewFlight();
        Assert.NotNull(w.NextToMention(Lat, Lon, 0, sights));
    }

    [Fact]
    public void Over_karasjok_they_know_about_sametinget()
    {
        var seen = SightWatcher.Visible(69.48, 25.50, 180, WorldData.Sights, 6);
        Assert.Contains(seen, s => s.Sight.Name == "Sametinget");
    }

    [Fact]
    public void The_situation_note_carries_the_view_and_the_phase_of_flight()
    {
        var t = new Telemetry { Time = DateTimeOffset.Now, OnGround = false, AglFt = 500, VerticalFpm = 700, BankDeg = -25 };
        string note = Prompts.Situation(DateTimeOffset.Now, null, t, 80, null, [new SeenSight(new PointOfInterest("Iesjavri", 0, 0, "a lake"), 3, "on the left")]);
        Assert.Contains("climbing away after takeoff", note);
        Assert.Contains("banking 25 degrees to the left", note);
        Assert.Contains("Iesjavri on the left", note);
    }
}

public class FuelAdviceTests
{
    private static Mission Job(string from, string to) => new() { Phase = MissionPhase.Booked, Plan = new MissionPlan { PickupId = from, DropoffId = to } };

    [Fact]
    public void Lakselv_to_mollisjok_needs_the_trip_and_reserve_and_there_and_back_is_more()
    {
        var a = WorldData.ById("ENNA")!;
        var advice = FuelAdvisor.Advise(AircraftProfile.C172, a.Lat, a.Lon, Job("ENNA", "ENMP"), 0)!;
        // 35 nm at 115 kt plus 6 minutes is 0.4 h, 3.8 gal, plus taxi 1.4 and 30 minutes of reserve 4.75: about 10 gal.
        Assert.InRange(advice.OneWayGal, 9, 11);
        Assert.False(advice.FuelAtDestination);
        Assert.InRange(advice.RoundTripGal, advice.OneWayGal + 4, advice.OneWayGal + 7);
        Assert.True(advice.RoundTripFits);
    }

    [Fact]
    public void Being_somewhere_else_first_adds_the_leg_to_the_pickup()
    {
        var alta = WorldData.ById("ENAT")!;
        var banak = WorldData.ById("ENNA")!;
        var here = FuelAdvisor.Advise(AircraftProfile.C172, banak.Lat, banak.Lon, Job("ENNA", "ENMP"), 0)!;
        var away = FuelAdvisor.Advise(AircraftProfile.C172, alta.Lat, alta.Lon, Job("ENNA", "ENMP"), 0)!;
        Assert.True(away.OneWayGal > here.OneWayGal + 2);
    }

    [Fact]
    public void Fuel_you_cached_at_the_destination_counts_as_a_pump()
    {
        var a = WorldData.ById("ENNA")!;
        Assert.True(FuelAdvisor.Advise(AircraftProfile.C172, a.Lat, a.Lon, Job("ENNA", "ENMP"), 30)!.FuelAtDestination);
        Assert.True(FuelAdvisor.Advise(AircraftProfile.C172, a.Lat, a.Lon, Job("ENMP", "ENNA"), 0)!.FuelAtDestination);
    }

    [Fact]
    public void A_trip_too_long_for_the_tanks_says_so()
    {
        var kirkenes = WorldData.ById("ENKR")!;
        var far = new Mission { Phase = MissionPhase.Booked, Plan = new MissionPlan { PickupId = "ENKR", DropoffId = "ENAT" } };
        var advice = FuelAdvisor.Advise(AircraftProfile.C172 with { UsableFuelGal = 15 }, kirkenes.Lat, kirkenes.Lon, far, 0)!;
        Assert.False(advice.OneWayFits);
    }
}

public class TemperTests
{
    [Fact]
    public void Most_people_are_calm_and_some_have_a_short_fuse()
    {
        var all = Enumerable.Range(0, 1000).Select(i => CastFactory.Create(new Random(i)).Temper).ToList();
        int calm = all.Count(t => t <= 0.4), angry = all.Count(t => t > 0.7);
        Assert.True(calm > 450, $"calm {calm}");
        Assert.InRange(angry, 80, 300);
    }

    [Fact]
    public void An_angry_person_is_told_to_act_like_it()
    {
        var c = new CareerState();
        Assert.Contains("short fuse", Prompts.Speaker(new Person { Name = "A", Temper = 0.9 }, null, c, TalkMode.Phone, null, "no"));
        Assert.Contains("easy going", Prompts.Speaker(new Person { Name = "B", Temper = 0.1 }, null, c, TalkMode.Phone, null, "no"));
        Assert.Contains("not like customer service", Prompts.Speaker(new Person { Name = "B" }, null, c, TalkMode.Phone, null, "no"));
    }
}

public class PriceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(1));
    private static MissionPlan ThreePeople() => new() { Passengers = [new(), new(), new()] };

    [Fact]
    public void Per_person_price_multiplies_by_the_people_on_board()
    {
        var plan = ThreePeople();
        PlanPatcher.Apply(plan, new PlanPatch { PricePerPersonNok = 3500 }, Now);
        Assert.Equal(10500, plan.PriceNok);
        Assert.Equal(3500, plan.PricePerPersonNok);
    }

    [Fact]
    public void Whole_trip_price_stays_the_same_when_people_are_added()
    {
        var plan = ThreePeople();
        PlanPatcher.Apply(plan, new PlanPatch { PriceNok = 3500 }, Now);
        PlanPatcher.Apply(plan, new PlanPatch { PassengerCount = 4 }, Now);
        Assert.Equal(3500, plan.PriceNok);
        Assert.Null(plan.PricePerPersonNok);
    }

    [Fact]
    public void One_more_passenger_on_a_per_person_deal_means_one_more_fare()
    {
        var plan = ThreePeople();
        PlanPatcher.Apply(plan, new PlanPatch { PricePerPersonNok = 3500 }, Now);
        var changes = PlanPatcher.Apply(plan, new PlanPatch { PassengerCount = 4 }, Now);
        Assert.Equal(14000, plan.PriceNok);
        Assert.Contains(changes, c => c.Field == "price");
    }

    [Fact]
    public void Changing_your_mind_from_per_person_to_a_flat_price_works_both_ways()
    {
        var plan = ThreePeople();
        PlanPatcher.Apply(plan, new PlanPatch { PricePerPersonNok = 3500 }, Now);
        PlanPatcher.Apply(plan, new PlanPatch { PriceNok = 9000 }, Now);
        Assert.Equal(9000, plan.PriceNok);
        Assert.Null(plan.PricePerPersonNok);
        PlanPatcher.Apply(plan, new PlanPatch { PricePerPersonNok = 2000 }, Now);
        Assert.Equal(6000, plan.PriceNok);
    }

    [Fact]
    public void The_friend_who_turns_up_at_the_scale_pays_too()
    {
        var c = new CareerState();
        var flow = new MissionFlow(c);
        var m = new Mission
        {
            Plan = new MissionPlan { Passengers = [new(), new()], PricePerPersonNok = 3000, PriceNok = 6000 },
            Truth = new MissionTruth { Passengers = [new() { Kg = 80 }, new() { Kg = 80 }, new() { Kg = 90 }] },
        };
        flow.PutOnScale(m, Now);
        Assert.Equal(9000, m.Plan.PriceNok);
    }
}

public class NameTests
{
    [Fact]
    public void In_norwegian_mode_everyone_is_from_oslo_finnmark_or_sapmi()
    {
        CastFactory.LocalsOnly = true;
        try
        {
            var homes = new HashSet<string>();
            for (int i = 0; i < 500; i++)
            {
                var p = CastFactory.Create(new Random(i));
                Assert.Equal("no", p.Language);
                homes.Add(p.Home);
                if (p.Home == "Oslo") Assert.DoesNotContain("reindeer herder", p.Job);
                Assert.Equal(p.Home, CastFactory.Companion(new Random(i + 1), p).Home);
            }
            Assert.Equal(["Finnmark", "Oslo", "Sápmi"], homes.OrderBy(h => h));
        }
        finally { CastFactory.LocalsOnly = false; }
    }

    [Fact]
    public void A_stranger_with_a_foreign_name_becomes_local_but_someone_you_flew_stays()
    {
        CastFactory.LocalsOnly = true;
        try
        {
            var stranger = new Person { Name = "Kenji Tanaka", Language = "en", Nationality = "Japanese", VoiceId = "x" };
            string id = stranger.Id;
            CastFactory.MakeLocal(stranger, new Random(1));
            Assert.NotEqual("Kenji Tanaka", stranger.Name);
            Assert.Equal("no", stranger.Language);
            Assert.Equal(id, stranger.Id);
            Assert.Null(stranger.VoiceId);

            var regular = new Person { Name = "Kenji Tanaka", Language = "en", FlightsWithYou = 3 };
            CastFactory.MakeLocal(regular, new Random(1));
            Assert.Equal("Kenji Tanaka", regular.Name);
        }
        finally { CastFactory.LocalsOnly = false; }
    }

    [Fact]
    public void A_misheard_mollisjok_still_finds_the_place() => Assert.Equal("ENMP", WorldData.Find("Moloshock")?.Id);
}

public class FigureTests
{
    private static readonly string[] NoTitles = [];

    [Theory]
    [InlineData("female", 11, "Tarmac_Female_Winter_")]
    [InlineData("male", 1, "Tarmac_Male_Winter_")]
    [InlineData("female", 7, "Tarmac_Female_Summer_")]
    public void The_sims_own_people_match_gender_and_season(string gender, int month, string prefix)
        => Assert.StartsWith(prefix, Figures.Pick(gender, month, [], NoTitles, new Random(1)));

    [Fact]
    public void Built_in_titles_are_only_ones_the_users_scenery_is_known_to_place()
    {
        string[] known = ["Tarmac_Male_Summer_Hispanic", "Tarmac_Female_Summer_Hispanic", "Tarmac_Female_Winter_Caucasian", "Tarmac_Male_Winter_Caucasian",
            "Tarmac_Female_Summer_Indian", "Tarmac_Male_Summer_Caucasian", "Tarmac_Female_Summer_Caucasian"];
        for (int seed = 0; seed < 200; seed++)
            foreach (var g in new[] { "male", "female" })
                foreach (var month in new[] { 1, 7 })
                {
                    string t = Figures.Pick(g, month, [], NoTitles, new Random(seed));
                    if (month == 1) Assert.Contains(t, known);
                }
    }

    [Fact]
    public void Installed_passenger_figures_win_and_match_gender()
    {
        string[] installed = ["ENGM_passenger_F01_W", "ENGM_passenger_M03_W"];
        Assert.Equal("ENGM_passenger_F01_W", Figures.Pick("female", 1, installed, NoTitles, new Random(2)));
        Assert.Equal("ENGM_passenger_M03_W", Figures.Pick("male", 1, installed, NoTitles, new Random(2)));
    }

    [Fact]
    public void The_sims_list_is_trusted_over_the_guess()
    {
        string[] sim = ["Tarmac_Male_Winter_Arab", "Boat01"];
        Assert.Equal("Tarmac_Male_Winter_Arab", Figures.Pick("male", 1, [], sim, new Random(3)));
    }
}

public class WeatherTests
{
    [Fact]
    public void Metar_json_is_parsed_including_ceiling_and_gusts()
    {
        const string json = """
            [{"icaoId":"ENNA","obsTime":1794000000,"wdir":240,"wspd":12,"wgst":22,"visib":"6+","temp":-4,
              "clouds":[{"cover":"FEW","base":800},{"cover":"BKN","base":1200},{"cover":"OVC","base":3000}],"rawOb":"ENNA 061220Z"}]
            """;
        var (icao, p) = WeatherParse.ParseMetars(json).Single();
        Assert.Equal("ENNA", icao);
        Assert.Equal(1200, p.CeilingFt);
        Assert.Equal(22, p.GustKt);
        Assert.Equal(FlightCategory.Marginal, p.Category);
    }

    [Fact]
    public void Variable_wind_does_not_break_the_parser()
    {
        const string json = """[{"icaoId":"ENAT","wdir":"VRB","wspd":3,"visib":10,"clouds":[]}]""";
        var (_, p) = WeatherParse.ParseMetars(json).Single();
        Assert.Null(p.WindFromDeg);
        Assert.Equal(FlightCategory.Vfr, p.Category);
    }

    [Fact]
    public void Forecast_json_maps_hours_and_low_cover_to_a_ceiling()
    {
        const string json = """
            {"utc_offset_seconds":3600,"hourly":{"time":["2026-11-07T14:00"],"temperature_2m":[-3],"precipitation":[0.4],"cloud_cover_low":[90],
             "visibility":[2500],"wind_speed_10m":[15],"wind_gusts_10m":[28],"wind_direction_10m":[200],"snowfall":[0.7]}}
            """;
        var p = WeatherParse.ParseForecast(json).Single();
        Assert.Equal(FlightCategory.Below, p.Category);
        Assert.Contains("snow", p.Summary());
    }
}
