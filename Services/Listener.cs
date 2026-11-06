using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FlightCompanion.Game.Talk;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SileroVad;
using Whisper.net;

namespace FlightCompanion.Services;

/// <summary>
/// The microphone, end to end: capture, voice detection, transcription. Built to never go deaf quietly.
/// The old one went deaf for good when a call ended just before another started, or when the mic or Sonar restarted.
/// Every capture is its own object with its own handlers, so an old one shutting down cannot touch a new one, and a
/// watchdog reopens the mic whenever audio stops arriving or the Windows default device changes.
/// </summary>
public sealed class Listener : IDisposable, IMMNotificationClient
{
    private readonly AppSettings _settings;
    private readonly MMDeviceEnumerator _devices = new();
    private readonly SpeechGate _gate = new();
    private readonly object _gateLock = new();
    private readonly BlockingCollection<WorkItem> _work = new(new ConcurrentQueue<WorkItem>());
    private readonly Thread _worker;
    private readonly Timer _watchdog;
    private Capture? _capture;
    private volatile bool _wanted;
    private long _lastFrameTicks;
    private volatile bool _deviceChanged;
    private SileroVadRunner? _vad;
    private WhisperFactory? _factory;
    private readonly Dictionary<string, WhisperProcessor> _processors = [];
    private volatile bool _whisperReady;
    private volatile bool _busy;
    private int _framesSincePartial;
    private bool _disposed;
    private string _prompt = "Hei, dette er piloten. Vi flyr fra Lakselv til Mollisjok, Ravnastua og Karasjok, over Finnmarksvidda. Bagasje, kilo, kroner, drivstoff, Alta, Banak, Kautokeino, Mollisjok.";

    private sealed record WorkItem(float[] Samples, bool Final);

    public event Action<Health>? HealthChanged;
    /// <summary>The pilot started talking for real, not a cough. Whoever is speaking should stop.</summary>
    public event Action? PilotStartedTalking;
    public event Action<string>? Partial;
    public event Action<string, string>? Final;
    public event Action<float>? Level;

    public bool Muted { get; set; }
    /// <summary>True while a voice plays through speakers: then we must not listen, or the character hears itself.</summary>
    public bool HalfDuplexHold { get; set; }
    public string? PreferredLanguage { get; set; }
    public string LastLanguage { get; private set; } = "en";
    public string? DeviceName { get; private set; }

    public Listener(AppSettings settings, bool load = true)
    {
        _settings = settings;
        _devices.RegisterEndpointNotificationCallback(this);
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "whisper" };
        _worker.Start();
        _watchdog = new Timer(_ => Watch(), null, 1000, 1000);
        if (load) _ = Task.Run(LoadModelsAsync);
    }

    public void SetVocabulary(string words) => _prompt = words;

    private async Task LoadModelsAsync()
    {
        try
        {
            HealthChanged?.Invoke(new Health(HealthLevel.Working, Game.L.T("Loading speech recognition")));
            _vad = new SileroVadRunner();
            var model = SpeechModels.ById(_settings.SpeechModel);
            string path = Path.Combine(AppPaths.Models, model.FileName);
            if (!File.Exists(path)) await DownloadAsync(model, path);
            _factory = WhisperFactory.FromPath(path);
            _whisperReady = true;
            _work.Add(new WorkItem(new float[SpeechGate.SampleRate], false));   // warms it up: the first real sentence took 3.4 s without this
            Log.Info("Mic", "Speech recognition ready");
            ReportState();
        }
        catch (Exception ex)
        {
            Log.Error("Mic", "Speech recognition failed to load", ex);
            HealthChanged?.Invoke(new Health(HealthLevel.Problem, Game.L.F("Speech recognition failed: {0}", ex.Message)));
        }
    }

    /// <summary>One time only, and a broken connection continues where it stopped. The window shows how far it has come.</summary>
    private async Task DownloadAsync(SpeechModelInfo model, string path)
    {
        string part = path + ".part";
        long have = File.Exists(part) ? new FileInfo(part).Length : 0;
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FlightCompanion/1.0");
        using var req = new HttpRequestMessage(HttpMethod.Get, model.Url);
        if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        bool resuming = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!resuming) have = 0;
        long total = have + (resp.Content.Headers.ContentLength ?? model.ApproxMb * 1_000_000L);
        await using (var src = await resp.Content.ReadAsStreamAsync())
        await using (var dst = new FileStream(part, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write))
        {
            var buf = new byte[1 << 20];
            long done = have;
            int lastPercent = -1, n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                int percent = (int)(done * 100 / total);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    HealthChanged?.Invoke(new Health(HealthLevel.Working, Game.L.F("Downloading the speech model: {0}% of {1} MB, one time only", percent, model.ApproxMb)));
                }
            }
        }
        File.Move(part, path, true);
    }

    public void Start()
    {
        _wanted = true;
        Open();
    }

    public void Stop()
    {
        _wanted = false;
        Interlocked.Exchange(ref _capture, null)?.Dispose();
        lock (_gateLock) _gate.Reset();
        while (_work.TryTake(out _)) { }
        ReportState();
    }

    private void Open()
    {
        if (!_wanted || _disposed) return;
        Interlocked.Exchange(ref _capture, null)?.Dispose();
        try
        {
            using var device = _devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            DeviceName = device.FriendlyName;
            var capture = new Capture(device, OnFrame, () => _deviceChanged = true);
            _capture = capture;
            Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);
            lock (_gateLock) _gate.Reset();
            _vad?.Reset();
            Log.Info("Mic", $"Listening on {DeviceName}, {capture.Format}");
        }
        catch (Exception ex)
        {
            DeviceName = null;
            Log.Error("Mic", "Could not open the microphone", ex);
            HealthChanged?.Invoke(new Health(HealthLevel.Problem, Game.L.T("No microphone found. Check the Windows default recording device.")));
            return;
        }
        ReportState();
    }

    private void ReportState()
    {
        if (!_wanted) HealthChanged?.Invoke(new Health(HealthLevel.Off, DeviceName is null ? Game.L.T("Mic off") : Game.L.F("Mic off, will use {0}", DeviceName)));
        else if (!_whisperReady) HealthChanged?.Invoke(new Health(HealthLevel.Working, Game.L.T("Loading speech recognition")));
        else if (DeviceName is null) HealthChanged?.Invoke(new Health(HealthLevel.Problem, Game.L.T("No microphone found")));
        else HealthChanged?.Invoke(new Health(HealthLevel.Good, DeviceName));
    }

    private void Watch()
    {
        if (!_wanted || _disposed) return;
        bool silentTooLong = (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastFrameTicks)) > TimeSpan.FromSeconds(3).Ticks;
        if (_deviceChanged || silentTooLong)
        {
            Log.Info("Mic", _deviceChanged ? "Default device changed, reopening" : "No audio for 3 seconds, reopening");
            _deviceChanged = false;
            Open();
        }
    }

    private void OnFrame(float[] frame)
    {
        Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);
        float peak = 0;
        foreach (float s in frame) peak = Math.Max(peak, Math.Abs(s));
        Level?.Invoke(peak);
        var vad = _vad;
        if (vad is null || !_whisperReady || Muted || HalfDuplexHold) return;

        double prob = vad.Probability(frame);
        GateEvent ev;
        float[]? partialAudio = null, finalAudio = null;
        lock (_gateLock)
        {
            ev = _gate.Process(frame, prob);
            if (ev == GateEvent.SpeechEnded) finalAudio = _gate.CompletedUtterance;
            else if (_gate.IsActive && ++_framesSincePartial >= 28 && !_busy && _work.Count == 0)
            {
                _framesSincePartial = 0;
                partialAudio = _gate.Snapshot();
            }
        }
        if (ev == GateEvent.SpeechConfirmed) PilotStartedTalking?.Invoke();
        if (partialAudio is not null) _work.Add(new WorkItem(partialAudio, false));
        if (finalAudio is not null)
        {
            _framesSincePartial = 0;
            while (_work.TryTake(out _)) { }   // the final supersedes any partial still waiting
            _work.Add(new WorkItem(finalAudio, true));
        }
    }

    private void WorkLoop()
    {
        foreach (var item in _work.GetConsumingEnumerable())
        {
            _busy = true;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (text, lang) = Transcribe(item.Samples).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(text) || IsWhisperNoise(text)) continue;
                if (item.Final)
                {
                    LastLanguage = lang;
                    Log.Info("Mic", $"Heard ({lang}, {item.Samples.Length / 16} ms audio in {sw.ElapsedMilliseconds} ms): {text}");
                    Final?.Invoke(text, lang);
                }
                else Partial?.Invoke(text);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log.Error("Mic", "Transcription failed", ex); }
            finally { _busy = false; }
        }
    }

    /// <summary>Whisper invents subtitles over noise. These are its usual ghosts, never real pilot speech.</summary>
    private static bool IsWhisperNoise(string t)
    {
        string s = t.Trim().Trim('.', '!', ' ').ToLowerInvariant();
        return s.Length == 0 || s.StartsWith('[') || s.StartsWith('(') || s is "thank you" or "thanks for watching" or "takk for meg" or "takk for at du så på" or "you" || s.StartsWith("teksting")
            || System.Text.RegularExpressions.Regex.IsMatch(s, @"^(u+h+|e+h+|h+m+|m+|a+h+|ø+h*|e+)[\s.,!?]*$");
    }

    private async Task<(string text, string lang)> Transcribe(float[] samples)
    {
        if (_factory is null) return ("", LastLanguage);
        // Pinning the language on every clip matters: free detection reads a short "ja" as German.
        string lang = PreferredLanguage ?? "auto";
        if (!_processors.TryGetValue(lang, out var proc))
        {
            proc = _factory.CreateBuilder().WithLanguage(lang).WithPrompt(_prompt).WithNoSpeechThreshold(0.6f).Build();
            _processors[lang] = proc;
        }
        var sb = new StringBuilder();
        string found = lang == "auto" ? LastLanguage : lang;
        await foreach (var seg in proc.ProcessAsync(samples))
        {
            sb.Append(seg.Text);
            if (lang == "auto" && !string.IsNullOrEmpty(seg.Language)) found = seg.Language;
        }
        // The pilot flies in English or Norwegian. Anything else is a detector mistake on a short clip.
        if (found is not ("en" or "no" or "nb" or "nn")) found = LastLanguage;
        if (found is "nb" or "nn") found = "no";
        return (sb.ToString().Trim(), found);
    }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string id)
    {
        if (flow == DataFlow.Capture && role == Role.Multimedia) _deviceChanged = true;
    }
    void IMMNotificationClient.OnDeviceAdded(string id) { }
    void IMMNotificationClient.OnDeviceRemoved(string id) { }
    void IMMNotificationClient.OnDeviceStateChanged(string id, DeviceState state) { }
    void IMMNotificationClient.OnPropertyValueChanged(string id, PropertyKey key) { }

    public void Dispose()
    {
        _disposed = true;
        _watchdog.Dispose();
        Stop();
        _work.CompleteAdding();
        _worker.Join(2000);
        try { _devices.UnregisterEndpointNotificationCallback(this); } catch (Exception ex) { Log.Error("Mic", "Unregister failed", ex); }
        _devices.Dispose();
        foreach (var p in _processors.Values) p.Dispose();
        _factory?.Dispose();
        _vad?.Dispose();
    }

    /// <summary>One open microphone. Converts anything Windows gives to 32 ms frames of 16 kHz mono with a real resampling filter.</summary>
    private sealed class Capture : IDisposable
    {
        private readonly WasapiCapture _capture;
        private readonly BufferedWaveProvider _in;
        private readonly ISampleProvider _resampled;
        private readonly Action<float[]> _onFrame;
        private readonly float[] _frame = new float[SpeechGate.FrameSamples];
        private readonly float[] _read = new float[SpeechGate.FrameSamples];
        private int _have;
        private int _closed;
        public string Format { get; }

        public Capture(MMDevice device, Action<float[]> onFrame, Action onLost)
        {
            _onFrame = onFrame;
            _capture = new WasapiCapture(device, true, 20);
            var fmt = _capture.WaveFormat;
            Format = $"{fmt.SampleRate} Hz, {fmt.Channels} channels, {fmt.BitsPerSample} bit {fmt.Encoding}";
            _in = new BufferedWaveProvider(fmt) { BufferDuration = TimeSpan.FromSeconds(2), DiscardOnBufferOverflow = true, ReadFully = false };
            ISampleProvider s = new ToMono(_in.ToSampleProvider());
            _resampled = fmt.SampleRate == SpeechGate.SampleRate ? s : new WdlResamplingSampleProvider(s, SpeechGate.SampleRate);
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, e) =>
            {
                if (Volatile.Read(ref _closed) == 0 && e.Exception is not null)
                {
                    Log.Error("Mic", "The microphone stopped", e.Exception);
                    onLost();
                }
            };
            _capture.StartRecording();
        }

        private void OnData(object? sender, WaveInEventArgs e)
        {
            if (Volatile.Read(ref _closed) != 0) return;
            _in.AddSamples(e.Buffer, 0, e.BytesRecorded);
            int n;
            while ((n = _resampled.Read(_read, 0, _read.Length)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    _frame[_have++] = _read[i];
                    if (_have == _frame.Length)
                    {
                        _onFrame((float[])_frame.Clone());
                        _have = 0;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            _capture.DataAvailable -= OnData;
            // Windows can take a moment to stop a device, so this happens beside the audio path, on this object only.
            var c = _capture;
            _ = Task.Run(() =>
            {
                try { c.StopRecording(); c.Dispose(); }
                catch (Exception ex) { Log.Error("Mic", "Closing the old capture failed", ex); }
            });
        }

        private sealed class ToMono(ISampleProvider src) : ISampleProvider
        {
            private float[] _buf = [];
            public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(src.WaveFormat.SampleRate, 1);

            public int Read(float[] buffer, int offset, int count)
            {
                int ch = src.WaveFormat.Channels;
                if (ch == 1) return src.Read(buffer, offset, count);
                int want = count * ch;
                if (_buf.Length < want) _buf = new float[want];
                int got = src.Read(_buf, 0, want);
                int frames = got / ch;
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0;
                    for (int c = 0; c < ch; c++) sum += _buf[f * ch + c];
                    buffer[offset + f] = sum / ch;
                }
                return frames;
            }
        }
    }
}

/// <summary>Silero voice detection, one 32 ms frame at a time, on the CPU so it does not touch the sim's GPU.</summary>
public sealed class SileroVadRunner : IDisposable
{
    private readonly VadModel _model;
    private readonly VadState _state;

    public SileroVadRunner()
    {
        VadRuntime.EnsureNativeRuntimeAvailable();
        _model = new VadModel(BundledModel());
        _state = _model.CreateState(SpeechGate.SampleRate, 1);
    }

    /// <summary>The package carries its models as resources. This takes the current one, not the legacy v4.</summary>
    private static byte[] BundledModel()
    {
        var manager = new System.Resources.ResourceManager("SileroVad.Properties.Resources", typeof(Vad).Assembly);
        var set = manager.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture, true, true)
            ?? throw new InvalidOperationException("The voice detection package has no resources");
        foreach (System.Collections.DictionaryEntry e in set)
        {
            if (e.Value is byte[] bytes && !e.Key.ToString()!.Contains("v4", StringComparison.OrdinalIgnoreCase))
                return bytes;
        }
        throw new InvalidOperationException("The voice detection package has no model");
    }

    public double Probability(float[] frame) => _model.DetectSpeech(frame, _state, SpeechGate.SampleRate);

    public void Reset() => _state.Reset();

    public void Dispose() => _model.Dispose();
}
