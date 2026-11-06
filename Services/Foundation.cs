using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace FlightCompanion.Services;

public static class AppPaths
{
    public static readonly string Roaming = Dir(Environment.SpecialFolder.ApplicationData);
    public static readonly string Local = Dir(Environment.SpecialFolder.LocalApplicationData);
    public static string Career => Path.Combine(Roaming, "career.json");
    public static string Settings => Path.Combine(Roaming, "settings.json");
    public static string Keys => Path.Combine(Roaming, "keys.dat");
    public static string Models => Sub(Local, "Models");
    public static string VoiceCache => Sub(Local, "VoiceCache");
    public static string MapTiles => Sub(Local, "MapTiles");
    public static string Logs => Sub(Local, "Logs");

    /// <summary>FLIGHTCOMPANION_HOME moves all personal data somewhere else, which is how a clean first start is tested.</summary>
    private static string Dir(Environment.SpecialFolder f)
        => Environment.GetEnvironmentVariable("FLIGHTCOMPANION_HOME") is { Length: > 0 } home
            ? Sub(home, f == Environment.SpecialFolder.ApplicationData ? "Roaming" : "Local")
            : Sub(Environment.GetFolderPath(f), "FlightCompanion");

    private static string Sub(string parent, string name)
    {
        string p = Path.Combine(parent, name);
        Directory.CreateDirectory(p);
        return p;
    }
}

/// <summary>Plain file log with a timestamp. Timing lines make latency measurable from real calls.</summary>
public static class Log
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 4096);
    private static readonly Thread Writer = StartWriter();

    private static Thread StartWriter()
    {
        var t = new Thread(() =>
        {
            foreach (var line in Queue.GetConsumingEnumerable())
            {
                try { File.AppendAllText(Path.Combine(AppPaths.Logs, $"FlightCompanion_{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine); }
                catch (IOException) { }
            }
        }) { IsBackground = true, Name = "log" };
        t.Start();
        return t;
    }

    public static void Info(string area, string text) => Queue.TryAdd($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{area}] {text}");
    public static void Error(string area, string text, Exception? ex = null)
        => Queue.TryAdd($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{area}] ERROR {text}{(ex is null ? "" : $": {ex.GetType().Name}: {ex.Message}")}");
}

/// <summary>API keys, encrypted for this Windows user, kept out of the project folder and out of git.</summary>
public sealed class KeyStore
{
    private sealed class Stored
    {
        public string? Anthropic { get; set; }
        public string? ElevenLabs { get; set; }
    }

    private Stored _s = new();
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FlightCompanion keys");

    public KeyStore() => Load();

    public string? Anthropic
    {
        get => _s.Anthropic;
        set { _s.Anthropic = Trim(value); Save(); }
    }

    public string? ElevenLabs
    {
        get => _s.ElevenLabs;
        set { _s.ElevenLabs = Trim(value); Save(); }
    }

    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private void Load()
    {
        try
        {
            if (File.Exists(AppPaths.Keys))
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.Keys), Entropy, DataProtectionScope.CurrentUser);
                _s = JsonSerializer.Deserialize<Stored>(plain) ?? new Stored();
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException) { Log.Error("Keys", "Could not read the key file", ex); }
    }

    private void Save()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_s);
        File.WriteAllBytes(AppPaths.Keys, ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
    }
}

public sealed class AppSettings
{
    /// <summary>The model that plays people. Fast comes first in a live voice call, and this can be changed in settings.</summary>
    public string SpeakerModel { get; set; } = "claude-haiku-5-5";
    /// <summary>"no": everyone speaks Norwegian. "en": everyone speaks English. "mixed": tourists English, locals Norwegian.</summary>
    public string TalkLanguage { get; set; } = "no";
    /// <summary>The model that listens along and fills in the plan card.</summary>
    public string ScribeModel { get; set; } = "claude-haiku-5-5";
    public bool DoNotDisturb { get; set; }
    /// <summary>Playing through speakers instead of a headset: never listen while someone is speaking, or they hear themselves.</summary>
    public bool UsingSpeakers { get; set; }
    /// <summary>Which speech recognition model listens to you. See <see cref="SpeechModels"/>.</summary>
    public string SpeechModel { get; set; } = SpeechModels.Norwegian.Id;
    public bool MicMuted { get; set; }
    public double VoiceVolume { get; set; } = 1.0;
    /// <summary>1 is the voice's normal pace. Slightly under, because a stream of quick sentences on a phone is hard to follow.</summary>
    public double VoiceSpeed { get; set; } = 0.92;
    /// <summary>Which ElevenLabs model reads the lines. A cloned voice can sound quite different from one model to the next.</summary>
    public string VoiceModel { get; set; } = ElevenLabs.BestModel;
    /// <summary>Makes calls sound like a phone and the intercom like a headset. Off plays the voice exactly as generated.</summary>
    public bool RadioEffect { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException) { Log.Error("Settings", "Could not read settings", ex); }
        return new AppSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(AppPaths.Settings, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (IOException ex) { Log.Error("Settings", "Could not save settings", ex); }
    }
}

public sealed record SpeechModelInfo(string Id, string Label, string FileName, string Url, int ApproxMb);

public static class SpeechModels
{
    public static readonly SpeechModelInfo Norwegian = new("nb-large-q5", "NB-Whisper (norsk, Nasjonalbiblioteket)", "ggml-nb-whisper-large-q5.bin",
        "https://huggingface.co/NbAiLab/nb-whisper-large/resolve/main/ggml-model-q5_0.bin", 1081);
    public static readonly SpeechModelInfo Fast = new("large-v3-turbo", "Whisper Turbo (raskere, mer svensk)", "ggml-large-v3-turbo.bin",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin", 1624);

    public static readonly SpeechModelInfo[] All = [Norwegian, Fast];
    public static SpeechModelInfo ById(string? id) => All.FirstOrDefault(m => m.Id == id) ?? Norwegian;
}

public enum HealthLevel { Off, Working, Good, Problem }

/// <summary>One line of honest status for the UI: the mic, the AI, the voices, the sim.</summary>
public sealed record Health(HealthLevel Level, string Text);

public static class Hashing
{
    public static string Short(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..16];
}
