using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FlightCompanion.Game.Career;
using FlightCompanion.Game.People;
using FlightCompanion.Game.World;

namespace FlightCompanion.Game.Missions;

/// <summary>A trip you chose yourself. The same choice gives the same job on any PC: same caller, route, companions and surprises.</summary>
public sealed record TripChoice
{
    public required string TemplateId { get; init; }
    public required string FromId { get; init; }
    public required string ToId { get; init; }
    public required int Seed { get; init; }
    public required Person Caller { get; init; }
}

/// <summary>A trip as a line of text you can send to a friend. Nothing about your history with the person goes along.</summary>
public static class TripCode
{
    private const string Prefix = "FC1-";

    private sealed record Wire(string T, string F, string O, int S, CallerWire C);
    private sealed record CallerWire(string Name, string Nationality, string Home, string Language, bool SpeaksNorwegian, string Gender, int Age, string Job,
        string Personality, string SpeechStyle, double WeightKg, double Fear, double Chatty, double Honesty, double Temper, double Wealth, string? VoiceId);

    public static string Encode(TripChoice t)
    {
        var p = t.Caller;
        var wire = new Wire(t.TemplateId, t.FromId, t.ToId, t.Seed, new CallerWire(p.Name, p.Nationality, p.Home, p.Language, p.SpeaksNorwegian, p.Gender, p.Age, p.Job,
            p.Personality, p.SpeechStyle, p.WeightKg, p.Fear, p.Chatty, p.Honesty, p.Temper, p.Wealth, p.VoiceId));
        using var packed = new MemoryStream();
        using (var z = new DeflateStream(packed, CompressionLevel.SmallestSize)) z.Write(JsonSerializer.SerializeToUtf8Bytes(wire));
        return Prefix + Convert.ToBase64String(packed.ToArray()).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>Null if the text is not a trip code, or it names something this version does not have.</summary>
    public static TripChoice? Decode(string? code)
    {
        string text = (code ?? "").Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        try
        {
            string b64 = text[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            using var z = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
            var w = JsonSerializer.Deserialize<Wire>(z);
            if (w?.C is null || string.IsNullOrWhiteSpace(w.C.Name) || MissionCatalog.ById(w.T) is null || WorldData.ById(w.F) is null || WorldData.ById(w.O) is null) return null;
            var c = w.C;
            return new TripChoice
            {
                TemplateId = w.T, FromId = w.F, ToId = w.O, Seed = w.S,
                Caller = new Person
                {
                    Name = c.Name, Nationality = c.Nationality, Home = c.Home, Language = c.Language, SpeaksNorwegian = c.SpeaksNorwegian, Gender = c.Gender, Age = c.Age, Job = c.Job,
                    Personality = c.Personality, SpeechStyle = c.SpeechStyle, WeightKg = c.WeightKg, Fear = c.Fear, Chatty = c.Chatty, Honesty = c.Honesty, Temper = c.Temper, Wealth = c.Wealth, VoiceId = c.VoiceId,
                },
            };
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException or IOException or NotSupportedException) { return null; }
    }

    /// <summary>Someone new for a trip, the same on both PCs because they travel inside the code.</summary>
    public static Person NewCaller(MissionTemplate tpl, Random rng) => CastFactory.Create(rng, tpl.CallerNationality ?? "Norwegian", tpl.CallerJob);

    /// <summary>Puts the trip into this career. Someone you already know by that name keeps their history with you.</summary>
    public static Mission Start(TripChoice t, CareerState c, DateTimeOffset now)
    {
        var tpl = MissionCatalog.ById(t.TemplateId)!;
        var caller = c.People.FirstOrDefault(p => p.Name == t.Caller.Name);
        if (caller is null)
        {
            caller = t.Caller;
            c.People.Add(caller);
        }
        else if (t.Caller.VoiceId is not null) caller.VoiceId = t.Caller.VoiceId;
        // One voice, one person: nobody else keeps talking with the voice this caller came with.
        if (caller.VoiceId is { } voice)
            foreach (var other in c.People.Where(p => p != caller && p.VoiceId == voice)) other.VoiceId = null;
        var m = MissionFactory.Create(tpl, new SameEverywhere(c), new Random(t.Seed), now, caller, WorldData.ById(t.FromId), WorldData.ById(t.ToId));
        c.Missions.Add(m);
        return m;
    }

    /// <summary>The companions come only from the seed, never from who this career happens to know.</summary>
    private sealed class SameEverywhere(CareerState state) : CareerStateView
    {
        public override double? CurrentLat => state.LastLat;
        public override double? CurrentLon => state.LastLon;
        public override Person PickCaller(MissionTemplate tpl, Random rng) => throw new InvalidOperationException("A chosen trip always has its caller");
        public override Person NewCompanion(Person lead, Random rng)
        {
            var p = CastFactory.Companion(rng, lead);
            state.People.Add(p);
            return p;
        }
    }
}
