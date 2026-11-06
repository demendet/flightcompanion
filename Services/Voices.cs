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

public sealed record VoiceInfo(string Id, string Name, string Gender, string Accent, string Age, string Category, string Language = "");

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
public sealed class ElevenLabs(KeyStore keys, AppSettings settings) : IVoiceStream
{
    public const string BestModel = "eleven_v4_turbo", FallbackModel = "eleven_flash_v2_5";
    private string _model = BestModel;
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
            ["text"] = text, ["model_id"] = _model, ["language_code"] = language,
            ["previous_text"] = previousText,
            ["voice_settings"] = new { stability = 0.45, similarity_boost = 0.75, style = 0.0, use_speaker_boost = false, speed = Math.Clamp(settings.VoiceSpeed, 0.7, 1.2) },
        });
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            string err = await resp.Content.ReadAsStringAsync(ct);
            // Accounts without the newest model still work, with the fast older one.
            if (_model == BestModel && resp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired && err.Contains("model", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info("Voice", $"{BestModel} is not available on this account, using {FallbackModel}: {err[..Math.Min(160, err.Length)]}");
                _model = FallbackModel;
                await StreamAsync(text, voiceId, language, previousText, onPcm, ct);
                return;
            }
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

    public async Task<string> GetRawAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add("xi-api-key", keys.ElevenLabs);
        using var resp = await Http.SendAsync(req, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new VoiceException(resp.StatusCode, $"ElevenLabs {(int)resp.StatusCode}: {body[..Math.Min(200, body.Length)]}");
        return body;
    }

    private static readonly string[] Spoken = ["northern", "nordnorsk", "oslo", "bergen", "trøndersk", "trondersk", "trondheim", "standard", "norwegian", "østnorsk", "vestnorsk"];

    /// <summary>
    /// Adds Norwegian voices from the ElevenLabs library to the account, a few of each gender: every northern one first, then the
    /// most used ones. People then sound Norwegian instead of an American voice reading Norwegian. Needs a paid plan. Costs no credits.
    /// </summary>
    public async Task<List<string>> AddNorwegianVoicesAsync(int eachGender, CancellationToken ct)
    {
        var added = new List<string>();
        var have = (await ListVoicesAsync(ct)).Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var gender in new[] { "male", "female" })
        {
            var found = new List<(string Owner, string Id, string Name, string Accent, string Note)>();
            for (int page = 0; page < 6; page++)
            {
                using var doc = JsonDocument.Parse(await GetRawAsync($"v1/shared-voices?language=no&gender={gender}&page_size=100&page={page}&sort=cloned_by_count", ct));
                foreach (var x in doc.RootElement.GetProperty("voices").EnumerateArray())
                {
                    string accent = x.TryGetProperty("accent", out var ac) ? ac.GetString() ?? "" : "";
                    // The filter also returns foreign voices that can read Norwegian, and "northern" alone can mean northern Vietnam.
                    // Only a voice whose own language is Norwegian counts.
                    string own = x.TryGetProperty("language", out var la) ? la.GetString() ?? "" : "";
                    if (own is not ("no" or "nb" or "nn") || !Spoken.Any(k => accent.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
                    found.Add((x.GetProperty("public_owner_id").GetString() ?? "", x.GetProperty("voice_id").GetString() ?? "", x.GetProperty("name").GetString() ?? "", accent,
                        x.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""));
                }
                if (!doc.RootElement.TryGetProperty("has_more", out var more) || !more.GetBoolean()) break;
            }
            static int Rank(string accent, string note) => accent.Contains("north", StringComparison.OrdinalIgnoreCase) || note.Contains("nordnorsk", StringComparison.OrdinalIgnoreCase) || note.Contains("northern", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            int count = 0;
            foreach (var f in found.OrderBy(f => Rank(f.Accent, f.Note)))
            {
                if (count >= eachGender) break;
                if (have.Contains(f.Name)) { count++; continue; }
                using var req = new HttpRequestMessage(HttpMethod.Post, $"v1/voices/add/{f.Owner}/{f.Id}");
                req.Headers.Add("xi-api-key", keys.ElevenLabs);
                req.Content = JsonContent.Create(new { new_name = f.Name });
                using var resp = await Http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync(ct);
                    Log.Error("Voice", $"Could not add {f.Name}: {err[..Math.Min(220, err.Length)]}");
                    if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired or HttpStatusCode.Unauthorized) throw new VoiceException(resp.StatusCode, err[..Math.Min(220, err.Length)]);
                    continue;
                }
                added.Add($"{f.Name} ({gender}, {f.Accent})");
                count++;
            }
        }
        return added;
    }

    public async Task RemoveVoiceAsync(string voiceId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"v1/voices/{voiceId}");
        req.Headers.Add("xi-api-key", keys.ElevenLabs);
        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new VoiceException(resp.StatusCode, $"ElevenLabs {(int)resp.StatusCode}");
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
                v.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "", L("language")));
        }
        return list;
    }
}

/// <summary>Gives each person a voice for life. Tries to match gender and origin, and never gives two regulars the same voice while others are free.</summary>
public sealed class VoiceCaster
{
    private readonly ElevenLabs _api;
    private static readonly string[] NorwegianAccents = ["norwegian", "oslo", "bergen", "trondheim", "trøndersk", "tromsø", "tromso", "nordnorsk", "northern", "finnmark"];
    private static readonly string[] NorthernAccents = ["northern", "tromsø", "tromso", "nordnorsk", "finnmark", "nord-norge"];
    private readonly HashSet<string> _banned = [];
    private List<VoiceInfo> _voices = [];
    private readonly Random _rng = new();

    public VoiceCaster(ElevenLabs api) => _api = api;

    public int Count => _voices.Count;
    public IReadOnlyList<VoiceInfo> Voices => _voices;

    public async Task RefreshAsync(CancellationToken ct) => _voices = await _api.ListVoicesAsync(ct);

    /// <summary>The plan does not allow this voice: it will not be cast again.</summary>
    public void Ban(string voiceId) => _banned.Add(voiceId);

    /// <summary>A voice's own language decides. The accent label alone is not enough: "northern" also means northern Vietnam.</summary>
    private static bool IsNorwegian(VoiceInfo v) => v.Language.Length > 0
        ? v.Language is "no" or "nb" or "nn"
        : NorwegianAccents.Any(a => a != "northern" && v.Accent.Contains(a, StringComparison.OrdinalIgnoreCase));

    public string? VoiceFor(Person p, IEnumerable<Person> others)
    {
        var usable = _voices.Where(v => !_banned.Contains(v.Id)).ToList();
        bool IsNorsk(VoiceInfo v) => IsNorwegian(v);
        if (p.VoiceId is not null && !_banned.Contains(p.VoiceId))
        {
            // Someone Norwegian who was given a foreign voice gets a Norwegian one as soon as the account has any.
            var current = usable.FirstOrDefault(v => v.Id == p.VoiceId);
            bool upgrade = p.Language == "no" && current is not null && !IsNorsk(current) && usable.Any(IsNorsk);
            if (!upgrade) return p.VoiceId;
        }
        if (usable.Count == 0) return null;
        // A voice someone cloned themselves often has no gender label. It plays male characters unless it is labelled otherwise,
        // or it would be the only match for every woman from up north.
        var same = usable.Where(v => (v.Gender.Length == 0 ? "male" : v.Gender).Equals(p.Gender, StringComparison.OrdinalIgnoreCase)).ToList();
        if (same.Count == 0) same = usable;
        string want = p.Nationality switch { "American" => "american", "British" => "british", "German" => "german", "Italian" => "italian", "Japanese" => "japanese", "Norwegian" or "Sami" => "norwegian", "Finnish" => "finnish", "Dutch" => "dutch", _ => "" };
        var accent = want.Length > 0 ? same.Where(v => v.Accent.Contains(want, StringComparison.OrdinalIgnoreCase)).ToList() : [];
        // A voice labelled Norwegian beats everything for someone who speaks it. The northern ones win for people from up north.
        var norsk = same.Where(IsNorwegian).ToList();
        var north = norsk.Where(v => NorthernAccents.Any(a => v.Accent.Contains(a, StringComparison.OrdinalIgnoreCase) || v.Name.Contains(a, StringComparison.OrdinalIgnoreCase))).ToList();
        var oslo = norsk.Where(v => v.Accent.Contains("oslo", StringComparison.OrdinalIgnoreCase)).ToList();
        var pool = p.Language != "no" ? (accent.Count > 0 ? accent : same)
            : p.Home == "Oslo" && oslo.Count > 0 ? oslo
            : p.Home != "Oslo" && north.Count > 0 ? north
            : p.Home != "Oslo" && norsk.Any(v => !v.Accent.Contains("oslo", StringComparison.OrdinalIgnoreCase)) ? norsk.Where(v => !v.Accent.Contains("oslo", StringComparison.OrdinalIgnoreCase)).ToList()
            : norsk.Count > 0 ? norsk : same;
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
