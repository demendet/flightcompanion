using FlightCompanion.Game.Aviation;

namespace FlightCompanion.Game.Flight;

/// <summary>One sample from the sim. The app layer fills it, the game never touches SimConnect.</summary>
public sealed record Telemetry
{
    public required DateTimeOffset Time { get; init; }
    public double Lat { get; init; }
    public double Lon { get; init; }
    public double AltFt { get; init; }
    public double AglFt { get; init; }
    public double GroundSpeedKt { get; init; }
    public double IasKt { get; init; }
    public double VerticalFpm { get; init; }
    public double PitchDeg { get; init; }
    public double BankDeg { get; init; }
    public double HeadingTrue { get; init; }
    public double GForce { get; init; } = 1;
    public bool OnGround { get; init; }
    public bool ParkingBrake { get; init; }
    public bool EngineRunning { get; init; }
    public bool OnFoot { get; init; }
    public bool StallWarning { get; init; }
    public bool OverspeedWarning { get; init; }
    public double FuelGal { get; init; }
    public double TotalWeightLb { get; init; }
    public double CgPercent { get; init; }
    public double WindFromDeg { get; init; }
    public double WindKt { get; init; }
    public double OatC { get; init; }
    public double TouchdownFpm { get; init; }
    public string Title { get; init; } = "";
}

public enum FlightEventKind
{
    Takeoff, Landing, SteepBank, ExtremeBank, StallWarning, Overspeed, HighG, NegativeG, DiveHard, LowPass,
    Turbulence, EngineStoppedInAir, LowFuel, Sightseeing,
}

public sealed record FlightEvent(FlightEventKind Kind, DateTimeOffset When, double Value, string Text);

public enum LandingGrade { Butter, Good, Firm, Hard, Crash }

public static class LandingScale
{
    public static LandingGrade Grade(double fpm, int bounces)
    {
        double a = Math.Abs(fpm);
        var g = a < 90 ? LandingGrade.Butter : a < 220 ? LandingGrade.Good : a < 420 ? LandingGrade.Firm : a < 800 ? LandingGrade.Hard : LandingGrade.Crash;
        if (bounces >= 2 && g < LandingGrade.Firm) g = LandingGrade.Firm;
        return g;
    }

    public static string Words(LandingGrade g) => g switch
    {
        LandingGrade.Butter => "Butter smooth",
        LandingGrade.Good => "A good landing",
        LandingGrade.Firm => "A firm landing",
        LandingGrade.Hard => "A hard landing",
        _ => "A landing that broke something",
    };
}

/// <summary>
/// Turns the raw stream into things worth reacting to, with hysteresis so a turn at 46 degrees is one event, not ten a second.
/// Also keeps the passengers' comfort, which recovers slowly when the flying is gentle.
/// </summary>
public sealed class FlightMonitor
{
    private const double SteepBankDeg = 40, ExtremeBankDeg = 58, HighG = 1.8, LowG = 0.35;
    private static readonly TimeSpan BounceWindow = TimeSpan.FromSeconds(4);

    private Telemetry? _last;
    private bool _airborne;
    private DateTimeOffset? _leftGroundAt;
    private readonly Dictionary<FlightEventKind, DateTimeOffset> _cooldown = [];
    private DateTimeOffset? _touchdownAt;
    private double _firstTouchFpm;
    private double _touchdownG;
    private int _bounces;
    private readonly Queue<(DateTimeOffset t, double g)> _gHistory = new();
    private bool _lowFuelFired;

    public double Comfort { get; private set; } = 100;
    public bool Airborne => _airborne;
    public double LastTouchdownFpm { get; private set; }
    public int LastBounces { get; private set; }
    public double LastTouchdownG { get; private set; }

    /// <summary>Starts a new flight's comfort from how nervous the people on board already are.</summary>
    public void BeginCabin(double fear) => Comfort = Math.Clamp(100 - fear * 20, 0, 100);

    public IReadOnlyList<FlightEvent> Process(Telemetry t, double reserveFuelGal = 4)
    {
        var events = new List<FlightEvent>();
        var prev = _last;
        _last = t;
        if (prev is null) { _airborne = !t.OnGround; return events; }
        double dt = Math.Clamp((t.Time - prev.Time).TotalSeconds, 0, 5);

        // Air to ground and back, with bounce counting.
        if (!t.OnGround && prev.OnGround) _leftGroundAt = t.Time;
        if (t.OnGround) _leftGroundAt = null;

        if (!_airborne && !t.OnGround && t.AglFt > 15 && _leftGroundAt is { } left && (t.Time - left).TotalSeconds > 1.5)
        {
            if (_touchdownAt is { } td && t.Time - td < BounceWindow) _bounces++;
            else
            {
                _airborne = true;
                _touchdownAt = null;
                events.Add(new(FlightEventKind.Takeoff, t.Time, t.TotalWeightLb, "Airborne"));
            }
        }
        if (t.OnGround && !prev.OnGround)
        {
            double fpm = t.TouchdownFpm != 0 ? t.TouchdownFpm : prev.VerticalFpm;
            if (_touchdownAt is null) { _firstTouchFpm = Math.Abs(fpm); _touchdownG = t.GForce; _bounces = 0; }
            else { _bounces++; _touchdownG = Math.Max(_touchdownG, t.GForce); }
            _touchdownAt = t.Time;
        }
        if (_airborne && _touchdownAt is { } touch && t.OnGround && t.Time - touch >= BounceWindow)
        {
            _airborne = false;
            _touchdownAt = null;
            LastTouchdownFpm = _firstTouchFpm;
            LastBounces = _bounces;
            LastTouchdownG = _touchdownG;
            var grade = LandingScale.Grade(_firstTouchFpm, _bounces);
            Comfort -= grade switch { LandingGrade.Hard => 25, LandingGrade.Firm => 6, LandingGrade.Crash => 60, _ => 0 };
            events.Add(new(FlightEventKind.Landing, t.Time, _firstTouchFpm,
                $"{LandingScale.Words(grade)}, {_firstTouchFpm:0} feet per minute at touchdown{(_bounces > 0 ? $", {_bounces} bounces" : "")}"));
        }

        if (_airborne)
        {
            double bank = Math.Abs(t.BankDeg);
            if (bank > ExtremeBankDeg) Fire(events, FlightEventKind.ExtremeBank, t, bank, $"Banking {bank:0} degrees, very steep", 12, 14);
            else if (bank > SteepBankDeg) Fire(events, FlightEventKind.SteepBank, t, bank, $"Banking {bank:0} degrees", 20, 5);
            if (t.StallWarning) Fire(events, FlightEventKind.StallWarning, t, t.IasKt, $"Stall warning horn at {t.IasKt:0} knots", 10, 14);
            if (t.OverspeedWarning) Fire(events, FlightEventKind.Overspeed, t, t.IasKt, $"Overspeed at {t.IasKt:0} knots", 15, 12);
            if (t.GForce > HighG) Fire(events, FlightEventKind.HighG, t, t.GForce, $"Pulling {t.GForce:0.0} G", 10, 8);
            if (t.GForce < LowG) Fire(events, FlightEventKind.NegativeG, t, t.GForce, $"Floating out of the seats at {t.GForce:0.0} G", 8, 15);
            if (t.VerticalFpm < -1500 && t.AglFt < 3000) Fire(events, FlightEventKind.DiveHard, t, t.VerticalFpm, $"Diving at {-t.VerticalFpm:0} feet per minute", 25, 8);
            if (t.AglFt is > 5 and < 100 && t.GroundSpeedKt > 60 && t.VerticalFpm > -300) Fire(events, FlightEventKind.LowPass, t, t.AglFt, $"Only {t.AglFt:0} feet over the ground", 40, 5);
            if (!t.EngineRunning && prev.EngineRunning) Fire(events, FlightEventKind.EngineStoppedInAir, t, t.AltFt, "The engine stopped in flight", 60, 40);

            _gHistory.Enqueue((t.Time, t.GForce));
            while (_gHistory.Count > 0 && (t.Time - _gHistory.Peek().t).TotalSeconds > 4) _gHistory.Dequeue();
            if (_gHistory.Count >= 15)
            {
                double mean = _gHistory.Average(x => x.g);
                double sd = Math.Sqrt(_gHistory.Average(x => (x.g - mean) * (x.g - mean)));
                if (sd > 0.22) Fire(events, FlightEventKind.Turbulence, t, sd, "Bumpy air", 40, 6);
            }

            if (t.FuelGal > 0 && t.FuelGal < reserveFuelGal && !_lowFuelFired)
            {
                _lowFuelFired = true;
                events.Add(new(FlightEventKind.LowFuel, t.Time, t.FuelGal, $"Only {t.FuelGal:0.0} gallons left"));
            }

            // Gentle flying slowly calms people down.
            if (bank < 25 && Math.Abs(t.GForce - 1) < 0.25) Comfort = Math.Min(100, Comfort + dt * 0.08);
        }
        if (!_airborne && t.FuelGal >= reserveFuelGal) _lowFuelFired = false;
        Comfort = Math.Clamp(Comfort, 0, 100);
        return events;
    }

    private void Fire(List<FlightEvent> events, FlightEventKind kind, Telemetry t, double value, string text, double cooldownS, double comfortHit)
    {
        if (_cooldown.TryGetValue(kind, out var until) && t.Time < until) return;
        _cooldown[kind] = t.Time.AddSeconds(cooldownS);
        Comfort -= comfortHit;
        events.Add(new(kind, t.Time, value, text));
    }
}
