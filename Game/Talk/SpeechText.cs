using System.Text;
using System.Text.RegularExpressions;

namespace FlightCompanion.Game.Talk;

/// <summary>Cuts a streaming reply into sentences as they arrive, so the first one can be spoken before the rest exists.</summary>
public sealed class SentenceSplitter
{
    private static readonly string[] NoBreakAfter = ["mr", "mrs", "ms", "dr", "st", "nr", "ca", "kl", "bl.a", "f.eks", "e.g", "i.e"];
    private const int LongClause = 110;
    private readonly StringBuilder _buf = new();

    public IEnumerable<string> Push(string delta)
    {
        _buf.Append(delta);
        var found = new List<string>();
        int start = 0;
        string s = _buf.ToString();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool enders = c is '.' or '!' or '?' or '…';
            bool clause = c is ',' or ';' or ':' && i - start > LongClause;
            if (!enders && !clause) continue;
            // Need to see what follows, or we might cut 3.5 or "Dr." in half.
            if (i + 1 >= s.Length) break;
            char next = s[i + 1];
            if (enders && !char.IsWhiteSpace(next) && next is not ('"' or '\'' or ')')) continue;
            if (c == '.' && IsAbbreviation(s, start, i)) continue;
            string sentence = s[start..(i + 1)].Trim();
            if (sentence.Length > 0) found.Add(sentence);
            start = i + 1;
        }
        if (start > 0) { _buf.Clear(); _buf.Append(s[start..]); }
        return found;
    }

    public string? Flush()
    {
        string rest = _buf.ToString().Trim();
        _buf.Clear();
        return rest.Length > 0 ? rest : null;
    }

    private static bool IsAbbreviation(string s, int start, int dot)
    {
        int w = dot - 1;
        while (w >= start && !char.IsWhiteSpace(s[w])) w--;
        string word = s[(w + 1)..dot].ToLowerInvariant();
        return NoBreakAfter.Contains(word);
    }
}

public static partial class SpeechText
{
    [GeneratedRegex(@"\*[^*]*\*|\[[^\]]*\]|\([^)]*\)|[#_`~]")]
    private static partial Regex Stage();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    /// <summary>Removes stage directions and markup a model may slip in, since a voice would read them aloud.</summary>
    public static string Clean(string s) => Spaces().Replace(Stage().Replace(s, " "), " ").Trim();

    /// <summary>The share of a sentence a listener heard before being cut off, rounded to whole words.</summary>
    public static string HeardPart(string sentence, double fraction)
    {
        if (fraction >= 0.98) return sentence;
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int keep = (int)Math.Round(words.Length * Math.Clamp(fraction, 0, 1));
        return keep == 0 ? "" : string.Join(' ', words.Take(keep)) + " [cut off]";
    }

    /// <summary>Which language the voice should use. Replaces the old word guessing, which read "their" as Norwegian "hei".</summary>
    public static string VoiceLanguage(string personLanguage, bool speaksNorwegian, string? pilotLanguage)
    {
        if (!speaksNorwegian && personLanguage != "no") return "en";
        if (pilotLanguage is "en" or "no") return pilotLanguage;
        return personLanguage;
    }
}

public enum GateEvent { None, SpeechStarted, SpeechConfirmed, SpeechEnded }

/// <summary>
/// Decides when the pilot starts and stops talking, from the voice detector's per-frame probability.
/// A frame is 32 ms. Keeps some audio from before the start so first syllables are not lost.
/// </summary>
public sealed class SpeechGate
{
    public const int SampleRate = 16000;
    public const int FrameSamples = 512;
    private const double StartProb = 0.5, EndProb = 0.35;
    private const int StartFrames = 3;           // about 100 ms: this starts collecting
    private const int ConfirmFrames = 7;         // about 220 ms: sure enough to interrupt whoever is talking
    private const int EndFrames = 14;            // about 450 ms of quiet ends the turn
    private const int PreRollFrames = 10;        // about 320 ms of lead-in
    private const int MinSpeechFrames = 8;
    private const int MaxFrames = 30 * 31;       // nobody talks for over 30 seconds without a breath

    private readonly Queue<float[]> _pre = new();
    private readonly List<float> _speech = [];
    private int _above, _below, _speechFrames, _total;
    private bool _active, _confirmed;

    public bool IsActive => _active;
    public float[]? CompletedUtterance { get; private set; }

    /// <summary>The audio so far, for live captions while someone is still talking.</summary>
    public float[] Snapshot() => _speech.ToArray();

    public GateEvent Process(float[] frame, double speechProbability)
    {
        if (!_active)
        {
            _pre.Enqueue(frame);
            while (_pre.Count > PreRollFrames) _pre.Dequeue();
            _above = speechProbability >= StartProb ? _above + 1 : 0;
            if (_above < StartFrames) return GateEvent.None;

            _active = true;
            _confirmed = false;
            _below = 0;
            _speechFrames = _above;
            _total = _pre.Count;
            _speech.Clear();
            foreach (var f in _pre) _speech.AddRange(f);
            _pre.Clear();
            return GateEvent.SpeechStarted;
        }

        _speech.AddRange(frame);
        _total++;
        if (speechProbability < EndProb) _below++;
        else { _below = 0; if (speechProbability >= StartProb) _speechFrames++; }

        if (!_confirmed && _speechFrames >= ConfirmFrames)
        {
            _confirmed = true;
            return GateEvent.SpeechConfirmed;
        }
        if (_below >= EndFrames || _total >= MaxFrames)
        {
            bool real = _speechFrames >= MinSpeechFrames;
            CompletedUtterance = real ? _speech.ToArray() : null;
            Reset();
            return real ? GateEvent.SpeechEnded : GateEvent.None;
        }
        return GateEvent.None;
    }

    public void Reset()
    {
        _active = false;
        _confirmed = false;
        _speech.Clear();
        _pre.Clear();
        _above = _below = _speechFrames = _total = 0;
    }
}
