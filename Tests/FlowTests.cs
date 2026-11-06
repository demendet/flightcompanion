using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.Career;
using FlightCompanion.Game.Flight;
using FlightCompanion.Game.Missions;
using FlightCompanion.Game.People;
using FlightCompanion.Game.World;
using Xunit;

namespace FlightCompanion.Tests;

/// <summary>A whole job, flown by numbers: the same telemetry the sim sends, through the same code the game runs.</summary>
public class FlowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 12, 0, 0, TimeSpan.FromHours(1));
    private static readonly Place From = WorldData.ById("ENNA")!;
    private static readonly Place To = WorldData.ById("ENMP")!;

    private sealed class Rig
    {
        public readonly CareerState Career = new();
        public readonly MissionFlow Flow;
        public readonly FlightMonitor Monitor = new();
        public readonly Person Caller = new() { Name = "Klaus", WeightKg = 90, Wealth = 0.8 };
        public readonly Mission M;
        public readonly List<FlowEvent> Events = [];
        public double Seconds;

        public Rig()
        {
            Flow = new MissionFlow(Career);
            Career.People.Add(Caller);
            M = new Mission
            {
                Title = "Test", TemplateId = "lodge_weekend", CallerId = Caller.Id, Phase = MissionPhase.Booked, CreatedAt = T0,
                Plan = new MissionPlan
                {
                    PickupId = From.Id, DropoffId = To.Id, PickupTime = T0, PriceNok = 6000, BaggageKg = 20, Agreed = true,
                    Passengers = [new() { PersonId = Caller.Id, Name = "Klaus" }],
                },
                Truth = new MissionTruth
                {
                    Passengers = [new() { PersonId = Caller.Id, Name = "Klaus", Kg = 90 }], BaggageKg = 42, ClaimedBaggageKg = 20,
                },
            };
            Career.Missions.Add(M);
        }

        public DateTimeOffset Now => T0.AddSeconds(Seconds);

        public void Feed(double lat, double lon, bool ground, double gs = 0, double agl = 0, double vs = 0, bool engine = true,
            bool onFoot = false, double fuel = 30, double weight = 2300, double td = 0, double every = 1, int repeat = 1)
        {
            for (int i = 0; i < repeat; i++)
            {
                Seconds += every;
                var t = new Telemetry
                {
                    Time = Now, Lat = lat, Lon = lon, OnGround = ground, GroundSpeedKt = gs, AglFt = agl, VerticalFpm = vs, EngineRunning = engine,
                    OnFoot = onFoot, FuelGal = fuel, TotalWeightLb = weight, TouchdownFpm = td, Title = "Cessna Skyhawk 172",
                };
                var flight = Monitor.Process(t);
                Events.AddRange(Flow.Step(t, flight, Now, Monitor.Comfort));
            }
        }
    }

    [Fact]
    public void At_a_small_strip_people_are_there_when_you_get_close_and_not_before()
    {
        var r = new Rig();
        r.M.Plan.PickupId = To.Id;
        r.Feed(To.Lat + 1, To.Lon, false, 110, 3000, repeat: 3);
        Assert.DoesNotContain(r.Events, e => e is PeopleShouldAppear);
        r.Feed(To.Lat + 0.03, To.Lon, false, 90, 800);
        Assert.Single(r.Events, e => e is PeopleShouldAppear);
        r.Feed(To.Lat + 0.02, To.Lon, false, 90, 600);
        Assert.Single(r.Events, e => e is PeopleShouldAppear);
    }

    [Fact]
    public void At_an_airport_they_come_to_wherever_you_stop()
    {
        var r = new Rig();
        var taxiway = Geo.Offset(From.Lat, From.Lon, 45, 1500);
        r.Feed(taxiway.lat, taxiway.lon, true, 15, repeat: 3);
        Assert.DoesNotContain(r.Events, e => e is PeopleShouldAppear or ArrivedAtPickup);   // still rolling
        r.Feed(taxiway.lat, taxiway.lon, true, 0);
        var appear = Assert.Single(r.Events.OfType<PeopleShouldAppear>());
        Assert.Single(r.Events, e => e is ArrivedAtPickup);
        Assert.InRange(Geo.DistanceM(taxiway.lat, taxiway.lon, appear.Lat, appear.Lon), 5, 30);
        Assert.Equal(appear.Lat, r.M.MeetLat);
    }

    [Fact]
    public void Stopping_on_the_far_side_of_the_region_does_not_count_as_arriving()
    {
        var r = new Rig();
        var away = Geo.Offset(From.Lat, From.Lon, 45, 6000);
        r.Feed(away.lat, away.lon, true, 0);
        Assert.Equal(MissionPhase.Booked, r.M.Phase);
    }

    [Fact]
    public void Nobody_waits_at_the_strip_days_ahead_of_their_booking()
    {
        var r = new Rig();
        r.M.Plan.PickupTime = T0.AddDays(3);
        r.Feed(From.Lat, From.Lon, true, 0);
        Assert.DoesNotContain(r.Events, e => e is PeopleShouldAppear or ArrivedAtPickup);
        Assert.Equal(MissionPhase.Booked, r.M.Phase);
    }

    [Fact]
    public void A_whole_job_runs_from_arrival_to_landing()
    {
        var r = new Rig();
        r.Feed(From.Lat, From.Lon, true, 0);
        var arrived = Assert.Single(r.Events.OfType<ArrivedAtPickup>());
        Assert.Equal(MissionPhase.AtPickup, r.M.Phase);

        r.Feed(From.MeetingLat, From.MeetingLon, true, 0, engine: false);
        Assert.Single(r.Events, e => e is CanMeetInPerson);
        Assert.Equal(MissionPhase.Boarding, r.M.Phase);

        r.Flow.PutOnScale(r.M, r.Now);
        Assert.Equal(42, r.M.Plan.BaggageKg);
        Assert.True(r.M.Plan.BaggageWeighed);

        r.Flow.CommitLoad(r.M, r.Now);
        Assert.Equal(MissionPhase.Loaded, r.M.Phase);

        r.Feed(From.Lat, From.Lon, true, 40, repeat: 2);
        r.Feed(From.Lat + 0.01, From.Lon, false, 70, 100, 700, fuel: 30, repeat: 4);
        Assert.Single(r.Events, e => e is MissionAirborne);
        Assert.Equal(MissionPhase.Airborne, r.M.Phase);

        r.Feed(To.Lat + 0.02, To.Lon, false, 90, 300, -400, fuel: 24, repeat: 5);
        r.Feed(To.Lat, To.Lon, false, 60, 8, -150, fuel: 24);
        r.Feed(To.Lat, To.Lon, true, 55, 0, 0, fuel: 24, td: -120);
        r.Feed(To.Lat, To.Lon, true, 30, 0, 0, fuel: 24, repeat: 5);
        var landed = Assert.Single(r.Events.OfType<MissionLanded>());
        Assert.True(landed.AtDestination);
        Assert.Equal(MissionPhase.Landed, r.M.Phase);
        Assert.Equal(120, r.M.Score.TouchdownFpm, 0);
        Assert.Equal(6, r.M.Score.FuelBurnedGal, 0);

        r.Feed(To.Lat, To.Lon, true, 0, 0, 0, engine: false, fuel: 24, repeat: 2);
        Assert.Single(r.Events, e => e is ReadyToDeplane);

        var review = ReviewMaker.Make(r.M, r.Career, r.Caller, r.Now);
        double cashBefore = r.Career.CashNok;
        ReviewMaker.Settle(r.M, review, r.Career, r.Now);
        Assert.Equal(MissionPhase.Complete, r.M.Phase);
        Assert.Equal(1, r.Caller.FlightsWithYou);
        Assert.True(r.Career.CashNok >= cashBefore + 6000);
        Assert.Equal(1, r.Career.FlightsCompleted);
        Assert.True(r.Career.HoursFlown > 0);
    }

    [Fact]
    public void Walking_over_on_foot_starts_the_meeting_only_when_close()
    {
        var r = new Rig();
        r.Feed(From.Lat, From.Lon, true, 0);
        var far = Geo.Offset(From.MeetingLat, From.MeetingLon, 90, 200);
        r.Feed(far.lat, far.lon, true, 0, engine: true, onFoot: true);
        Assert.DoesNotContain(r.Events, e => e is CanMeetInPerson);
        var near = Geo.Offset(From.MeetingLat, From.MeetingLon, 90, 20);
        r.Feed(near.lat, near.lon, true, 0, engine: true, onFoot: true);
        Assert.Single(r.Events, e => e is CanMeetInPerson);
    }

    [Fact]
    public void Taking_off_without_loading_leaves_them_standing_there_still_booked()
    {
        var r = new Rig();
        r.Feed(From.Lat, From.Lon, true, 0);
        Assert.Equal(MissionPhase.AtPickup, r.M.Phase);
        r.Feed(From.Lat + 0.01, From.Lon, false, 70, 100, 700, repeat: 4);
        Assert.Equal(MissionPhase.Booked, r.M.Phase);
        Assert.Null(r.M.ArrivedAtPickup);
    }

    [Fact]
    public void Landing_somewhere_else_counts_against_you()
    {
        var r = new Rig();
        r.Feed(From.Lat, From.Lon, true, 0);
        r.Flow.CommitLoad(r.M, r.Now);
        r.Feed(From.Lat + 0.01, From.Lon, false, 70, 100, 700, repeat: 4);
        r.Feed(69.0, 23.0, false, 60, 8, -150);
        r.Feed(69.0, 23.0, true, 55, 0, 0, td: -100);
        r.Feed(69.0, 23.0, true, 20, repeat: 5);
        var landed = Assert.Single(r.Events.OfType<MissionLanded>());
        Assert.False(landed.AtDestination);
        var review = ReviewMaker.Make(r.M, r.Career, r.Caller, r.Now);
        Assert.Contains(review.Points, p => p.Contains("where agreed"));
    }

    [Fact]
    public void Overweight_takeoff_is_noticed_from_the_real_sim_weight()
    {
        var r = new Rig();
        r.Feed(From.Lat, From.Lon, true, 0);
        r.Flow.CommitLoad(r.M, r.Now);
        r.Feed(From.Lat + 0.01, From.Lon, false, 70, 100, 700, weight: 2650, repeat: 4);
        Assert.True(r.M.WentOverweight);
        Assert.Equal(100, r.M.LoadedOverGrossLb, 0);
    }

    [Fact]
    public void Touch_and_go_keeps_the_flight_going()
    {
        var r = new Rig();
        r.Feed(From.Lat, From.Lon, true, 0);
        r.Flow.CommitLoad(r.M, r.Now);
        r.Feed(From.Lat + 0.01, From.Lon, false, 70, 100, 700, repeat: 4);
        r.Feed(To.Lat, To.Lon, false, 60, 8, -150);
        r.Feed(To.Lat, To.Lon, true, 55, 0, 0, td: -100);
        r.Feed(To.Lat, To.Lon, true, 40, repeat: 5);
        Assert.Equal(MissionPhase.Landed, r.M.Phase);
        r.Feed(To.Lat + 0.01, To.Lon, false, 70, 100, 700, repeat: 4);
        Assert.Equal(MissionPhase.Airborne, r.M.Phase);
    }
}
