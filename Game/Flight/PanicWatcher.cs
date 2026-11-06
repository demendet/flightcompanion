namespace FlightCompanion.Game.Flight;

public enum PanicChange { None, Began, Ended, Crashed }

/// <summary>
/// Decides when the people on board stop talking and start screaming: far past a normal bank angle, nose down, a dive, heavy G,
/// or the ground coming up too fast. A normal approach and a steep but sane turn never set it off, and it has to last a moment
/// before it counts, so a gust does not either.
/// </summary>
public sealed class PanicWatcher
{
    private const double BankDeg = 60, NoseDownDeg = 40, DiveFpm = 2500, HighG = 3.5, LowG = -0.5;
    private const double ImpactSeconds = 15, ImpactFpm = 1500, CrashFpm = 800, HoldSeconds = 0.6, CalmSeconds = 3;

    private DateTimeOffset? _since;
    private DateTimeOffset _lastBad;
    private bool _wasAirborne;
    private double _lastFpm;

    public bool Panicking { get; private set; }

    /// <summary>Seconds until the ground at this descent, if that is short and the descent is hard. Never on a normal approach.</summary>
    public static bool ImpactComing(Telemetry t)
    {
        if (t.OnGround || t.VerticalFpm > -ImpactFpm) return false;
        return t.AglFt / (-t.VerticalFpm / 60) < ImpactSeconds;
    }

    public static bool OutOfControl(Telemetry t) =>
        !t.OnGround && (Math.Abs(t.BankDeg) > BankDeg || t.PitchDeg < -NoseDownDeg || t.VerticalFpm < -DiveFpm || t.GForce > HighG || t.GForce < LowG);

    public PanicChange Step(Telemetry t)
    {
        var change = PanicChange.None;
        if (_wasAirborne && t.OnGround && -_lastFpm > CrashFpm)
        {
            Panicking = false;
            _since = null;
            change = PanicChange.Crashed;
        }
        else if (ImpactComing(t) || OutOfControl(t))
        {
            _since ??= t.Time;
            _lastBad = t.Time;
            if (!Panicking && (ImpactComing(t) || (t.Time - _since.Value).TotalSeconds >= HoldSeconds))
            {
                Panicking = true;
                change = PanicChange.Began;
            }
        }
        else
        {
            _since = null;
            if (Panicking && (t.Time - _lastBad).TotalSeconds >= CalmSeconds)
            {
                Panicking = false;
                change = PanicChange.Ended;
            }
        }
        _wasAirborne = !t.OnGround;
        _lastFpm = t.VerticalFpm;
        return change;
    }

    /// <summary>The sim itself said we crashed.</summary>
    public void Reset()
    {
        Panicking = false;
        _since = null;
        _wasAirborne = false;
    }
}
