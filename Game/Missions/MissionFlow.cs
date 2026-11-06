using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.Career;
using FlightCompanion.Game.Flight;
using FlightCompanion.Game.World;

namespace FlightCompanion.Game.Missions;

public abstract record FlowEvent;
public sealed record PeopleShouldAppear(Mission Mission, Place Place) : FlowEvent;
public sealed record PeopleShouldDisappear(Mission Mission) : FlowEvent;
public sealed record ArrivedAtPickup(Mission Mission, double LateMinutes) : FlowEvent;
public sealed record CanMeetInPerson(Mission Mission) : FlowEvent;
public sealed record MissionAirborne(Mission Mission) : FlowEvent;
public sealed record MissionLanded(Mission Mission, bool AtDestination) : FlowEvent;
public sealed record ReadyToDeplane(Mission Mission) : FlowEvent;
public sealed record LoadChanged(double[] StationLb) : FlowEvent;

/// <summary>Drives one active mission from the sim's telemetry. Pure logic: same input, same result, so it can be tested without flying.</summary>
public sealed class MissionFlow(CareerState state)
{
    private const double SpawnKm = 6, ArriveM = 450, MeetM = 60, LeaveM = 800;
    private bool _peopleVisible;
    private string? _peopleFor;
    private bool _meetAnnounced;
    private bool _deplaneAnnounced;

    public Mission? Active => state.Missions
        .Where(m => m.Phase is MissionPhase.AtPickup or MissionPhase.Boarding or MissionPhase.Loaded or MissionPhase.Airborne or MissionPhase.Landed)
        .OrderBy(m => m.CreatedAt).FirstOrDefault();

    public IReadOnlyList<FlowEvent> Step(Telemetry t, IReadOnlyList<FlightEvent> flight, DateTimeOffset now, double comfort)
    {
        var events = new List<FlowEvent>();
        foreach (var m in state.Missions.Where(x => x.Phase == MissionPhase.Booked).OrderBy(x => x.Plan.PickupTime ?? DateTimeOffset.MaxValue).ToList())
            StepBooked(m, t, now, events);
        if (Active is { } a) StepActive(a, t, flight, now, comfort, events);
        return events;
    }

    private void StepBooked(Mission m, Telemetry t, DateTimeOffset now, List<FlowEvent> events)
    {
        if (m.Pickup is not { } p) return;
        double dist = Geo.DistanceM(t.Lat, t.Lon, p.MeetingLat, p.MeetingLon);
        // People wait at the strip only around their own time. Nobody stands out there for three days.
        bool timeRight = m.Plan.PickupTime is not { } due || (now - due).TotalHours is > -2 and < 3;
        if (timeRight && dist < SpawnKm * 1000 && _peopleFor != m.Id && m.Plan.Passengers.Count > 0)
        {
            _peopleVisible = true;
            _peopleFor = m.Id;
            events.Add(new PeopleShouldAppear(m, p));
        }
        double toStrip = Geo.DistanceM(t.Lat, t.Lon, p.Lat, p.Lon);
        if ((toStrip < ArriveM || dist < ArriveM) && t.OnGround && t.GroundSpeedKt < 25 && timeRight && Active is null)
        {
            m.Phase = MissionPhase.AtPickup;
            m.ArrivedAtPickup = now;
            double late = m.Plan.PickupTime is { } d ? Math.Max(0, (now - d).TotalMinutes) : 0;
            _meetAnnounced = false;
            _deplaneAnnounced = false;
            events.Add(new ArrivedAtPickup(m, late));
        }
    }

    private void StepActive(Mission m, Telemetry t, IReadOnlyList<FlightEvent> flight, DateTimeOffset now, double comfort, List<FlowEvent> events)
    {
        switch (m.Phase)
        {
            case MissionPhase.AtPickup:
            case MissionPhase.Boarding:
            {
                // Walking up to them starts the conversation. Parked with the engine off counts too, for aircraft or sims
                // where the character cannot walk, and the card offers a button.
                if (m.Pickup is { } p && !_meetAnnounced)
                {
                    double d = Geo.DistanceM(t.Lat, t.Lon, p.MeetingLat, p.MeetingLon);
                    bool walkedOver = t.OnFoot && d < MeetM;
                    bool parked = t.OnGround && !t.EngineRunning && t.GroundSpeedKt < 1;
                    if (walkedOver || parked)
                    {
                        _meetAnnounced = true;
                        m.Phase = MissionPhase.Boarding;
                        events.Add(new CanMeetInPerson(m));
                    }
                }
                if (flight.Any(f => f.Kind == FlightEventKind.Takeoff))
                {
                    // Left without loading. They are still standing there.
                    m.Phase = MissionPhase.Booked;
                    m.ArrivedAtPickup = null;
                }
                break;
            }
            case MissionPhase.Loaded:
                if (flight.Any(f => f.Kind == FlightEventKind.Takeoff))
                {
                    m.Phase = MissionPhase.Airborne;
                    m.AirborneAt = now;
                    m.FuelAtTakeoffGal = t.FuelGal;
                    double over = t.TotalWeightLb - AircraftProfile.ForTitle(t.Title).MaxGrossLb;
                    if (over > 0) { m.WentOverweight = true; m.LoadedOverGrossLb = over; }
                    events.Add(new MissionAirborne(m));
                }
                break;
            case MissionPhase.Airborne:
            {
                var s = m.Score;
                s.ComfortMin = Math.Min(s.ComfortMin, comfort);
                s.ComfortAvgSum += comfort;
                s.ComfortSamples++;
                s.MaxG = Math.Max(s.MaxG, t.GForce);
                s.MinG = Math.Min(s.MinG, t.GForce);
                foreach (var f in flight)
                {
                    if (f.Kind == FlightEventKind.StallWarning) s.StallWarnings++;
                    if (f.Kind is FlightEventKind.SteepBank or FlightEventKind.ExtremeBank) s.SteepTurns++;
                    if (f.Kind is FlightEventKind.HighG or FlightEventKind.NegativeG or FlightEventKind.DiveHard) s.HardMoments++;
                    if (f.Kind == FlightEventKind.Landing)
                    {
                        s.TouchdownFpm = f.Value;
                        m.LandedAt = now;
                        s.AirborneHours = m.AirborneAt is { } up ? (now - up).TotalHours : 0;
                        s.FuelBurnedGal = Math.Max(0, m.FuelAtTakeoffGal - t.FuelGal);
                        bool atDest = m.Dropoff is { } to && Geo.DistanceM(t.Lat, t.Lon, to.Lat, to.Lon) < 2500;
                        s.LandedAtDestination = atDest;
                        m.Phase = MissionPhase.Landed;
                        events.Add(new MissionLanded(m, atDest));
                    }
                }
                break;
            }
            case MissionPhase.Landed:
                if (flight.Any(f => f.Kind == FlightEventKind.Takeoff))
                {
                    // Touch and go, or a hop to the right strip after a diversion: the flight continues.
                    m.Phase = MissionPhase.Airborne;
                    _deplaneAnnounced = false;
                    break;
                }
                if (!_deplaneAnnounced && t.OnGround && !t.EngineRunning && t.GroundSpeedKt < 1)
                {
                    _deplaneAnnounced = true;
                    events.Add(new ReadyToDeplane(m));
                }
                break;
        }
        if (_peopleVisible && m.Phase is MissionPhase.Loaded or MissionPhase.Airborne && _peopleFor == m.Id)
            _peopleVisible = false;
        _ = comfort;
    }

    /// <summary>The pilot committed to the load: this is what the plane gets. The truth replaces the guesses.</summary>
    public void CommitLoad(Mission m, DateTimeOffset now)
    {
        m.Phase = MissionPhase.Loaded;
        m.LoadedAt = now;
        _peopleVisible = false;
    }

    public void PutOnScale(Mission m, DateTimeOffset now)
    {
        var plan = m.Plan;
        if (Math.Abs(plan.BaggageKg - m.Truth.BaggageKg) >= 0.5)
            plan.Changes.Add(new PlanChange(now, "baggage", $"{plan.BaggageKg:0} kg", $"{m.Truth.BaggageKg:0} kg on the scale"));
        plan.BaggageKg = m.Truth.BaggageKg;
        plan.CargoKg = m.Truth.CargoKg;
        plan.BaggageWeighed = true;
        // Everyone actually standing there is now on the card, and everyone steps on the scale.
        if (m.Truth.Passengers.Count != plan.Passengers.Count)
            plan.Changes.Add(new PlanChange(now, "passengers", plan.Passengers.Count.ToString(), $"{m.Truth.Passengers.Count} turned up"));
        plan.Passengers = m.Truth.Passengers.Select(p => new PlanPassenger { PersonId = p.PersonId, Name = p.Name, Kg = p.Kg, KgKnown = true }).ToList();
    }

    /// <summary>Stations as pounds for the sim, in book order. The app maps these to the aircraft's real station numbers by name.</summary>
    public static IReadOnlyList<StationLoad> LoadFor(Mission m, AircraftProfile a, double pilotKg)
        => PlanEvaluator.Evaluate(m, a, pilotKg).Load.Stations;
}
