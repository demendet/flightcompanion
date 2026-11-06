using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlightCompanion.Game.People;

namespace FlightCompanion.Services;

public sealed record VoiceInfo(string Id, string Name, string Gender, string Accent, string Age, string Category);

public sealed class VoiceException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public bool VoiceNotAllowed => Status == HttpStatusCode.PaymentRequired;
    public bool KeyProblem => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    public bool OutOfCredits => Message.Contains("quota", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// ElevenLabs, always streaming, on the Flash model: the first audio comes while the sentence is still being generated.
/// The old app used the non-streaming route and waited for the whole clip.
/// </summary>
public sealed class ElevenLabs(KeyStore keys)
{
    private const string Model = "eleven_flash_v2_5";
    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10), EnableMultipleHttp2Connections = true })
    {
        BaseAddress = new Uri("https://api.elevenlabs.io/"), Timeout = Timeout.InfiniteTimeSpan,
    };

    public bool HasKey => !string.IsNullOrEmpty(keys.ElevenLabs);

    public async Task StreamAsync(string text, string voiceId, string language, string? previousText, Action<byte[], int> onPcm, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"v1/text-to-speech/{voiceId}/stream?output_format=pcm_24000");
        req.Headers.Add("xi-api-key", keys.ElevenLabs);
        req.Content = JsonContent.Create(new Dictionary<string, object?>
        {
            ["text"] = text, ["model_id"] = Model, ["language_code"] = language,
            ["previous_text"] = previousText,
            ["voice_settings"] = new { stability = 0.45, similarity_boost = 0.75, style = 0.0, use_speaker_boost = false },
        });
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            string err = await resp.Content.ReadAsStringAsync(ct);
            throw new VoiceException(resp.StatusCode, $"ElevenLabs {(int)resp.StatusCode}: {err[..Math.Min(200, err.Length)]}");
        }
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var buf = new byte[4800];
        int carry = 0, n;
        while ((n = await stream.ReadAsync(buf.AsMemory(carry), ct)) > 0)
        {
            int have = carry + n, whole = have & ~1;   // samples are two bytes and a network chunk can split one
            onPcm(buf, whole);
            carry = have - whole;
            if (carry == 1) buf[0] = buf[whole];
        }
    }

    public async Task<byte[]> RenderAsync(string text, string voiceId, string language, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await StreamAsync(text, voiceId, language, null, (b, c) => ms.Write(b, 0, c), ct);
        return ms.ToArray();
    }

    public async Task<List<VoiceInfo>> ListVoicesAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "v1/voices");
        req.Headers.Add("xi-api-key", keys.ElevenLabs);
        using var resp = await Http.SendAsync(req, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new VoiceException(resp.StatusCode, $"ElevenLabs {(int)resp.StatusCode}: {body[..Math.Min(200, body.Length)]}");
        using var doc = JsonDocument.Parse(body);
        var list = new List<VoiceInfo>();
        foreach (var v in doc.RootElement.GetProperty("voices").EnumerateArray())
        {
            string L(string key) => v.TryGetProperty("labels", out var l) && l.ValueKind == JsonValueKind.Object && l.TryGetProperty(key, out var x) ? x.GetString() ?? "" : "";
            list.Add(new VoiceInfo(v.GetProperty("voice_id").GetString() ?? "", v.GetProperty("name").GetString() ?? "", L("gender"), L("accent"), L("age"),
                v.TryGetProperty("category", out var c) ? c.GetString() ?? "" : ""));
        }
        return list;
    }
}

/// <summary>Gives each person a voice for life. Tries to match gender and origin, and never gives two regulars the same voice while others are free.</summary>
public sealed class VoiceCaster
{
    private readonly ElevenLabs _api;
    private readonly HashSet<string> _banned = [];
    private List<VoiceInfo> _voices = [];
    private readonly Random _rng = new();

    public VoiceCaster(ElevenLabs api) => _api = api;

    public int Count => _voices.Count;
    public IReadOnlyList<VoiceInfo> Voices => _voices;

    public async Task RefreshAsync(CancellationToken ct) => _voices = await _api.ListVoicesAsync(ct);

    /// <summary>The plan does not allow this voice: it will not be cast again.</summary>
    public void Ban(string voiceId) => _banned.Add(voiceId);

    public string? VoiceFor(Person p, IEnumerable<Person> others)
    {
        if (p.VoiceId is not null && !_banned.Contains(p.VoiceId)) return p.VoiceId;
        var usable = _voices.Where(v => !_banned.Contains(v.Id)).ToList();
        if (usable.Count == 0) return null;
        var same = usable.Where(v => string.IsNullOrEmpty(v.Gender) || v.Gender.Equals(p.Gender, StringComparison.OrdinalIgnoreCase)).ToList();
        if (same.Count == 0) same = usable;
        string want = p.Nationality switch { "American" => "american", "British" => "british", "German" => "german", "Italian" => "italian", "Japanese" => "japanese", "Norwegian" or "Sami" => "norwegian", "Finnish" => "finnish", "Dutch" => "dutch", _ => "" };
        var accent = want.Length > 0 ? same.Where(v => v.Accent.Contains(want, StringComparison.OrdinalIgnoreCase)).ToList() : [];
        var pool = accent.Count > 0 ? accent : same;
        var taken = others.Select(o => o.VoiceId).Where(id => id is not null).ToHashSet();
        var free = pool.Where(v => !taken.Contains(v.Id)).ToList();
        var pick = (free.Count > 0 ? free : pool)[_rng.Next((free.Count > 0 ? free : pool).Count)];
        p.VoiceId = pick.Id;
        return pick.Id;
    }
}

/// <summary>Short clips kept on disk, so a gasp at a stall horn plays instantly and costs nothing the second time.</summary>
public sealed class ClipCache(ElevenLabs api)
{
    private string PathFor(string voice, string language, string text)
        => Path.Combine(AppPaths.VoiceCache, $"{voice}_{language}_{Hashing.Short(text)}.pcm");

    public byte[]? TryGet(string voice, string language, string text)
    {
        string p = PathFor(voice, language, text);
        return File.Exists(p) ? File.ReadAllBytes(p) : null;
    }

    public async Task EnsureAsync(string voice, string language, string text, CancellationToken ct)
    {
        string p = PathFor(voice, language, text);
        if (File.Exists(p)) return;
        byte[] pcm = await api.RenderAsync(text, voice, language, ct);
        await File.WriteAllBytesAsync(p, pcm, ct);
    }
}
