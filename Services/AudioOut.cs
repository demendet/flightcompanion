using System;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FlightCompanion.Services;

public enum VoiceFx { Phone, Intercom, Clear }

/// <summary>
/// One always-open output. Voices and tones are mixed into it, so starting speech costs nothing, and a stop is a buffer
/// clear on the audio thread's own data instead of disposing a device from another thread (that used to crash the app).
/// </summary>
public sealed class AudioOut : IDisposable, IMMNotificationClient
{
    private const int MixRate = 48000;
    private readonly object _gate = new();
    private readonly MixingSampleProvider _mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(MixRate, 2)) { ReadFully = true };
    private readonly MMDeviceEnumerator _devices = new();
    private WasapiOut? _out;
    private Timer? _reopen;
    private bool _disposed;

    public event Action<Health>? HealthChanged;
    public float Volume { get; set; } = 1f;

    public AudioOut()
    {
        _devices.RegisterEndpointNotificationCallback(this);
        Open();
    }

    private void Open()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                _out?.Dispose();
                using var device = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _out = new WasapiOut(device, AudioClientShareMode.Shared, true, 50);
                _out.Init(_mixer);
                _out.PlaybackStopped += (_, e) =>
                {
                    if (e.Exception is not null) ScheduleReopen();
                };
                _out.Play();
                HealthChanged?.Invoke(new Health(HealthLevel.Good, device.FriendlyName));
                Log.Info("Audio", $"Output on {device.FriendlyName}");
            }
            catch (Exception ex)
            {
                Log.Error("Audio", "Could not open the audio output", ex);
                HealthChanged?.Invoke(new Health(HealthLevel.Problem, Game.L.T("No audio output device")));
                ScheduleReopen();
            }
        }
    }

    private void ScheduleReopen()
    {
        _reopen?.Dispose();
        _reopen = new Timer(_ => Open(), null, 1000, Timeout.Infinite);
    }

    public VoiceChannel CreateVoice(VoiceFx fx) => new(this, fx);

    internal void AddInput(ISampleProvider s) => _mixer.AddMixerInput(s);
    internal void RemoveInput(ISampleProvider s) => _mixer.RemoveMixerInput(s);

    /// <summary>A ringing phone, until the handle is disposed. Generated, so there is no sound file to lose.</summary>
    public IDisposable Ring(bool outgoing = false)
    {
        var tone = outgoing ? new RingTone([425.0], 1.0, 4.0, 0.10f) : new RingTone([880.0, 1108.7], 0.18, 2.2, 0.16f, burst: 4);
        _mixer.AddMixerInput(tone);
        return new Stopper(() => _mixer.RemoveMixerInput(tone));
    }

    /// <summary>A short click for lines opening and closing. Connects the ear to what just changed.</summary>
    public void Beep(double hz = 660, double seconds = 0.12) => _mixer.AddMixerInput(new RingTone([hz], seconds, seconds, 0.12f, once: true));

    private sealed class Stopper(Action stop) : IDisposable
    {
        private Action? _stop = stop;
        public void Dispose() => Interlocked.Exchange(ref _stop, null)?.Invoke();
    }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) ScheduleReopen();
    }
    void IMMNotificationClient.OnDeviceAdded(string id) { }
    void IMMNotificationClient.OnDeviceRemoved(string id) { }
    void IMMNotificationClient.OnDeviceStateChanged(string id, DeviceState state) { }
    void IMMNotificationClient.OnPropertyValueChanged(string id, PropertyKey key) { }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _reopen?.Dispose();
            try { _devices.UnregisterEndpointNotificationCallback(this); } catch (Exception ex) { Log.Error("Audio", "Unregister failed", ex); }
            _out?.Dispose();
            _devices.Dispose();
        }
    }
}

/// <summary>A person's voice on the line: takes PCM as it arrives from the network, plays it right away, stops instantly.</summary>
public sealed class VoiceChannel : IDisposable
{
    public const int SampleRate = 24000;
    private readonly AudioOut _audio;
    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _chain;
    private long _queuedBytes;

    internal VoiceChannel(AudioOut audio, VoiceFx fx)
    {
        _audio = audio;
        _buffer = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromSeconds(120), DiscardOnBufferOverflow = true, ReadFully = true,
        };
        ISampleProvider s = _buffer.ToSampleProvider();
        s = new VoiceFilter(s, fx);
        s = new WdlResamplingSampleProvider(s, 48000);
        _chain = new MonoToStereoSampleProvider(s);
        audio.AddInput(_chain);
    }

    public void Push(byte[] pcm16, int count)
    {
        _buffer.AddSamples(pcm16, 0, count);
        Interlocked.Add(ref _queuedBytes, count);
    }

    public bool IsPlaying => _buffer.BufferedBytes > 0;
    public long QueuedBytes => Interlocked.Read(ref _queuedBytes);
    /// <summary>How far the listener has heard, measured in bytes pushed so far.</summary>
    public long HeardBytes => QueuedBytes - _buffer.BufferedBytes;

    public void Stop()
    {
        long waiting = _buffer.BufferedBytes;
        _buffer.ClearBuffer();
        Interlocked.Add(ref _queuedBytes, -waiting);
    }

    public void Dispose() => _audio.RemoveInput(_chain);
}

/// <summary>Band limits that make a call sound like a call and an intercom like an intercom.</summary>
internal sealed class VoiceFilter : ISampleProvider
{
    private readonly ISampleProvider _src;
    private readonly Biquad? _hp, _lp;
    private readonly float _drive;

    public VoiceFilter(ISampleProvider src, VoiceFx fx)
    {
        _src = src;
        int rate = src.WaveFormat.SampleRate;
        switch (fx)
        {
            case VoiceFx.Phone:
                _hp = Biquad.HighPass(rate, 320); _lp = Biquad.LowPass(rate, 3400); _drive = 1.6f;
                break;
            case VoiceFx.Intercom:
                _hp = Biquad.HighPass(rate, 240); _lp = Biquad.LowPass(rate, 5200); _drive = 1.3f;
                break;
            default:
                _drive = 1f;
                break;
        }
    }

    public WaveFormat WaveFormat => _src.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int n = _src.Read(buffer, offset, count);
        for (int i = offset; i < offset + n; i++)
        {
            float x = buffer[i];
            if (_hp is not null) x = _hp.Process(x);
            if (_lp is not null) x = _lp.Process(x);
            if (_drive > 1f) x = MathF.Tanh(x * _drive) / _drive * 1.15f;   // gentle clipping, what cheap audio paths really do
            buffer[i] = x;
        }
        return n;
    }
}

internal sealed class Biquad
{
    private readonly float _b0, _b1, _b2, _a1, _a2;
    private float _z1, _z2;

    private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = (float)(b0 / a0); _b1 = (float)(b1 / a0); _b2 = (float)(b2 / a0);
        _a1 = (float)(a1 / a0); _a2 = (float)(a2 / a0);
    }

    public float Process(float x)
    {
        float y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }

    public static Biquad LowPass(int rate, double hz)
    {
        double w = 2 * Math.PI * hz / rate, c = Math.Cos(w), a = Math.Sin(w) / (2 * 0.707);
        return new Biquad((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a);
    }

    public static Biquad HighPass(int rate, double hz)
    {
        double w = 2 * Math.PI * hz / rate, c = Math.Cos(w), a = Math.Sin(w) / (2 * 0.707);
        return new Biquad((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a);
    }
}

/// <summary>Tones with an on and off pattern. Reads silence during the off part, ends by itself when told to play once.</summary>
internal sealed class RingTone(double[] freqs, double onS, double periodS, float gain, int burst = 1, bool once = false) : ISampleProvider
{
    private long _n;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public int Read(float[] buffer, int offset, int count)
    {
        const double rate = 48000;
        for (int i = 0; i < count; i += 2)
        {
            double t = _n++ / rate;
            if (once && t >= onS) return i;
            double inPeriod = t % periodS;
            float s = 0;
            double slot = onS * 2;
            int index = burst > 1 ? (int)(inPeriod / slot) : 0;
            bool on = burst > 1 ? index < burst && inPeriod % slot < onS : inPeriod < onS;
            if (on)
            {
                double f = freqs[(burst > 1 ? index : 0) % freqs.Length];
                double local = burst > 1 ? inPeriod % slot : inPeriod;
                double env = Math.Min(1, Math.Min(local / 0.005, (onS - local) / 0.01));
                s = (float)(Math.Sin(2 * Math.PI * f * t) * gain * env);
            }
            buffer[offset + i] = s;
            buffer[offset + i + 1] = s;
        }
        return count;
    }
}
