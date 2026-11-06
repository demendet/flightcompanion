using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Whisper.net;

namespace FlightCompanion.Services
{
    // Offline speech-to-text using Whisper (whisper.cpp via Whisper.net), GPU-accelerated
    // when an NVIDIA/CUDA runtime is present (falls back to CPU). Captures the WINDOWS DEFAULT
    // recording device via WASAPI, resamples to 16 kHz mono, and STREAMS partial results while
    // you speak (SpeechRecognizing) plus a final transcription on pause (SpeechRecognized).
    // Auto-detects Norwegian and English. No API key, no cloud. Model downloaded once.
    public class WhisperSpeechRecognitionService : ISpeechRecognitionService, IDisposable
    {
        // large-v3-turbo: accurate Norwegian+English (small mangled it). Cached from earlier.
        private const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin";
        private const string ModelFileName = "ggml-large-v3-turbo.bin";

        private const int WhisperRate = 16000;
        private const float SpeechThreshold = 0.010f; // RMS above this counts as speech
        private const int SilenceHangMs = 600;         // silence after speech => final
        private const int MinUtteranceMs = 300;        // ignore blips shorter than this
        private const int PartialIntervalMs = 600;     // emit a live partial roughly this often while speaking

        private readonly IDebugLogService _logger;
        private WhisperFactory? _factory;
        private WhisperProcessor? _processor;
        private WasapiCapture? _capture;
        private WaveFormat? _captureFormat;

        private readonly List<float> _utterance = new();                 // current utterance (16 kHz mono)
        private readonly BlockingCollection<(float[] samples, bool isFinal)> _queue = new(new ConcurrentQueue<(float[], bool)>());
        private bool _isListening;
        private bool _isSpeaking;
        private int _silenceMs;
        private int _msSincePartial;
        private volatile bool _busy;
        private bool _modelReady;
        private bool _disposed;

        private float _peakRms;
        private DateTime _lastLevelLog = DateTime.MinValue;
        private DateTime _lastLevelEmit = DateTime.MinValue;

        public event EventHandler<string>? SpeechRecognized;   // final
        public event EventHandler<string>? SpeechRecognizing;  // live partial
        public event EventHandler<bool>? IsListeningChanged;
        public event EventHandler? SpeechStarted;              // user began talking (barge-in)
        public event EventHandler<float>? MicLevel;            // live level 0..1 for the UI bar

        public bool IsListening => _isListening;
        public bool Muted { get; set; } // true while the AI is speaking (half-duplex, stops self-echo)
        public string LastDetectedLanguage { get; private set; } = "en";

        public WhisperSpeechRecognitionService(IDebugLogService logger)
        {
            _logger = logger;
            _ = Task.Run(InitModelAsync);
            _ = Task.Run(ConsumeLoop);
        }

        private async Task InitModelAsync()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FlightCompanion", "Models");
                Directory.CreateDirectory(dir);
                var modelPath = Path.Combine(dir, ModelFileName);

                if (!File.Exists(modelPath) || new FileInfo(modelPath).Length < 100_000_000)
                {
                    _logger.LogVoice($"Downloading Whisper model (~1.6 GB, one time) -> {modelPath}");
                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
                    using var resp = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead);
                    resp.EnsureSuccessStatusCode();
                    var tmp = modelPath + ".part";
                    await using (var src = await resp.Content.ReadAsStreamAsync())
                    await using (var dst = File.Create(tmp))
                        await src.CopyToAsync(dst);
                    if (File.Exists(modelPath)) File.Delete(modelPath);
                    File.Move(tmp, modelPath);
                    _logger.LogVoice("Whisper model download complete.");
                }

                _factory = WhisperFactory.FromPath(modelPath);
                _processor = _factory.CreateBuilder().WithLanguage("auto").Build();
                _modelReady = true;
                _logger.LogVoice("Whisper ready — offline speech recognition (GPU if available, Norwegian + English auto-detect).");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to initialize Whisper speech recognition", ex, "Voice");
            }
        }

        public Task StartContinuousRecognitionAsync()
        {
            if (_isListening || _disposed) return Task.CompletedTask;
            try
            {
                if (!_modelReady)
                    _logger.LogWarning("Whisper model still loading/downloading — it will start recognizing once ready.", "Voice");

                MMDevice device;
                using (var mm = new MMDeviceEnumerator())
                    device = mm.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

                _capture = new WasapiCapture(device);
                _captureFormat = _capture.WaveFormat;
                _logger.LogVoice($"Mic capture device: \"{device.FriendlyName}\" — {_captureFormat.SampleRate} Hz, {_captureFormat.Channels} ch, {_captureFormat.Encoding}");

                _capture.DataAvailable += OnMicData;
                _capture.RecordingStopped += (_, __) => { _capture?.Dispose(); _capture = null; };
                _capture.StartRecording();

                _utterance.Clear();
                _isSpeaking = false;
                _silenceMs = 0;
                _msSincePartial = 0;
                _peakRms = 0;
                _isListening = true;
                IsListeningChanged?.Invoke(this, true);
                _logger.LogVoice("Whisper listening started (WASAPI default mic, streaming).");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to start Whisper listening", ex, "Voice");
            }
            return Task.CompletedTask;
        }

        public Task StopContinuousRecognitionAsync()
        {
            if (!_isListening) return Task.CompletedTask;
            try
            {
                _capture?.StopRecording();
                _isListening = false;
                _utterance.Clear();
                while (_queue.TryTake(out _)) { }
                IsListeningChanged?.Invoke(this, false);
                _logger.LogVoice("Whisper listening stopped.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to stop Whisper listening", ex, "Voice");
            }
            return Task.CompletedTask;
        }

        private void OnMicData(object? sender, WaveInEventArgs e)
        {
            if (_disposed || _captureFormat == null) return;

            var mono = ToMonoFloat(e.Buffer, e.BytesRecorded, _captureFormat);
            var samples = Resample(mono, _captureFormat.SampleRate, WhisperRate);
            if (samples.Length == 0) return;

            double sumSq = 0;
            for (int i = 0; i < samples.Length; i++) sumSq += samples[i] * samples[i];
            float rms = (float)Math.Sqrt(sumSq / samples.Length);
            if (rms > _peakRms) _peakRms = rms;

            // Live mic level for the on-screen bar (~15 Hz).
            var now = DateTime.UtcNow;
            if ((now - _lastLevelEmit).TotalMilliseconds >= 66)
            {
                _lastLevelEmit = now;
                MicLevel?.Invoke(this, Math.Min(1f, rms * 4f));
            }
            if ((now - _lastLevelLog).TotalMilliseconds >= 3000)
            {
                _lastLevelLog = now;
                _logger.LogVoice($"mic level (peak RMS ~{_peakRms:F3}){(_modelReady ? "" : " [model not ready]")}");
                _peakRms = 0;
            }

            if (!_modelReady) return;

            int blockMs = samples.Length * 1000 / WhisperRate;

            if (rms >= SpeechThreshold)
            {
                if (!_isSpeaking) SpeechStarted?.Invoke(this, EventArgs.Empty); // barge-in: you started talking
                _isSpeaking = true;
                _silenceMs = 0;
                _utterance.AddRange(samples);
                _msSincePartial += blockMs;

                // Emit a LIVE partial every ~PartialIntervalMs while speaking (only if the worker is idle).
                if (_msSincePartial >= PartialIntervalMs && !_busy && _queue.Count == 0 &&
                    _utterance.Count >= WhisperRate / 3)
                {
                    _msSincePartial = 0;
                    _queue.Add((_utterance.ToArray(), false));
                }
            }
            else if (_isSpeaking)
            {
                _utterance.AddRange(samples); // keep a little trailing silence
                _silenceMs += blockMs;
                if (_silenceMs >= SilenceHangMs)
                {
                    var utter = _utterance.ToArray();
                    _utterance.Clear();
                    _isSpeaking = false;
                    _silenceMs = 0;
                    _msSincePartial = 0;
                    int ms = utter.Length * 1000 / WhisperRate;
                    if (ms >= MinUtteranceMs)
                    {
                        while (_queue.TryTake(out _)) { } // drop stale partials
                        _queue.Add((utter, true));        // final
                    }
                }
            }
        }

        private async Task ConsumeLoop()
        {
            try
            {
                foreach (var (samples, isFinal) in _queue.GetConsumingEnumerable())
                {
                    _busy = true;
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var (text, lang) = await Transcribe(samples);
                        sw.Stop();
                        if (isFinal) _logger.LogVoice($"[timing] {samples.Length * 1000 / WhisperRate}ms audio transcribed in {sw.ElapsedMilliseconds}ms (GPU should be well under audio length)");
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            if (isFinal)
                            {
                                LastDetectedLanguage = lang;
                                _logger.LogVoice($"Whisper final ({lang}): {text}");
                                SpeechRecognized?.Invoke(this, text);
                            }
                            else
                            {
                                SpeechRecognizing?.Invoke(this, text);
                            }
                        }
                    }
                    catch (Exception ex) { _logger.LogError("Whisper transcription failed", ex, "Voice"); }
                    finally { _busy = false; }
                }
            }
            catch { }
        }

        private async Task<(string text, string lang)> Transcribe(float[] samples16k)
        {
            if (_processor == null) return (string.Empty, LastDetectedLanguage);
            var sb = new StringBuilder();
            string lang = LastDetectedLanguage;
            _logger.LogVoice($"transcribing {samples16k.Length * 1000 / WhisperRate}ms clip...");
            await foreach (var seg in _processor.ProcessAsync(samples16k))
            {
                if (!string.IsNullOrWhiteSpace(seg.Text)) sb.Append(seg.Text);
                if (!string.IsNullOrEmpty(seg.Language)) lang = seg.Language;
            }
            return (sb.ToString().Trim(), lang);
        }

        private static float[] ToMonoFloat(byte[] buffer, int bytes, WaveFormat fmt)
        {
            int ch = Math.Max(1, fmt.Channels);
            int bps = fmt.BitsPerSample / 8;
            if (bps == 0) return Array.Empty<float>();
            int frames = bytes / (bps * ch);
            var mono = new float[frames];
            bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat ||
                           (fmt.Encoding == WaveFormatEncoding.Extensible && fmt.BitsPerSample == 32);
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < ch; c++)
                {
                    int idx = (f * ch + c) * bps;
                    float s;
                    if (isFloat && bps == 4) s = BitConverter.ToSingle(buffer, idx);
                    else if (bps == 2) s = (short)(buffer[idx] | (buffer[idx + 1] << 8)) / 32768f;
                    else s = 0;
                    sum += s;
                }
                mono[f] = sum / ch;
            }
            return mono;
        }

        private static float[] Resample(float[] src, int srcRate, int dstRate)
        {
            if (src.Length == 0 || srcRate == dstRate) return src;
            double ratio = (double)dstRate / srcRate;
            int outLen = (int)(src.Length * ratio);
            if (outLen <= 0) return Array.Empty<float>();
            var outb = new float[outLen];
            for (int i = 0; i < outLen; i++)
            {
                double pos = i / ratio;
                int i0 = (int)pos;
                int i1 = Math.Min(i0 + 1, src.Length - 1);
                double frac = pos - i0;
                outb[i] = (float)(src[i0] * (1 - frac) + src[i1] * frac);
            }
            return outb;
        }

        public async Task<string> RecognizeOnceAsync()
        {
            await Task.CompletedTask;
            return string.Empty;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _queue.CompleteAdding(); } catch { }
            try { _capture?.StopRecording(); _capture?.Dispose(); } catch { }
            _processor?.Dispose();
            _factory?.Dispose();
        }
    }
}
