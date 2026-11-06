using System.Text.RegularExpressions;

namespace FlightCompanion.Game.Talk;

/// <summary>
/// Speech recognition knows Norwegian but not Finnmark: from real calls it wrote "målersjokk", "Mollersjok" and "Molesjok" for
/// Mollisjok, and "honningsåk" for Honningsvåg. This puts the right names back by how the words sound. It runs on the finished
/// text in microseconds, so it adds no delay. Ordinary words are left alone: short words are never touched, and a loose match
/// needs a long word.
/// </summary>
public sealed partial class NameFixer
{
    private const int MinNameLetters = 5, LooseFrom = 8, CloseFrom = 5;
    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase) { "North", "Northern", "Lights", "Cathedral", "Cape", "Park", "Airport", "Lodge", "Strip" };

    private readonly List<(string Name, string Sound)> _names = [];
    private readonly HashSet<string> _exact = new(StringComparer.OrdinalIgnoreCase);

    public NameFixer(IEnumerable<string> names)
    {
        foreach (var full in names)
            foreach (var word in Word().Matches(full).Select(w => w.Value))
                if (word.Length >= MinNameLetters && char.IsUpper(word[0]) && !NotNames.Contains(word) && _exact.Add(word))
                    _names.Add((word, Sound(word)));
    }

    public string Fix(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var words = Word().Matches(text).ToList();
        var result = new System.Text.StringBuilder(text.Length);
        int at = 0;
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            // Whisper sometimes breaks a strange name in two: "Mollis jokk".
            if (i + 1 < words.Count && words[i + 1].Index == w.Index + w.Length + 1 && text[w.Index + w.Length] == ' '
                && words[i + 1].Length >= 3 && !_exact.Contains(w.Value) && !_exact.Contains(words[i + 1].Value) && SoundsExactly(w.Value + words[i + 1].Value) is { } joined)
            {
                result.Append(text, at, w.Index - at).Append(joined);
                at = words[i + 1].Index + words[i + 1].Length;
                i++;
                continue;
            }
            if (Replacement(w.Value) is not { } name) continue;
            result.Append(text, at, w.Index - at).Append(name);
            at = w.Index + w.Length;
        }
        return result.Append(text, at, text.Length - at).ToString();
    }

    private string? SoundsExactly(string word)
    {
        string sound = Sound(word);
        foreach (var (name, nameSound) in _names)
            if (nameSound == sound) return name;
        return null;
    }

    private string? Replacement(string word)
    {
        if (word.Length < MinNameLetters || _exact.Contains(word)) return null;
        return Match(word, char.IsUpper(word[0]) ? 1 : 0);
    }

    /// <summary>Allowed sound differences: two for long words, one for a capitalised word of middling length, otherwise it must sound the same.</summary>
    private string? Match(string word, int close)
    {
        string sound = Sound(word);
        string? best = null;
        int bestDistance = int.MaxValue;
        bool tie = false;
        foreach (var (name, nameSound) in _names)
        {
            if (nameSound[0] != sound[0]) continue;
            int allowed = sound.Length >= LooseFrom ? 2 : sound.Length >= CloseFrom ? close : 0;
            int d = Distance(sound, nameSound, allowed);
            if (d > allowed) continue;
            if (d < bestDistance) { best = name; bestDistance = d; tie = false; }
            else if (d == bestDistance && best != name) tie = true;
        }
        return tie ? null : best;
    }

    /// <summary>How a Norwegian word sounds, near enough: å and ø and o alike, sj and skj alike, doubled letters as one.</summary>
    public static string Sound(string word)
    {
        string s = word.ToLowerInvariant().Replace("skj", "s").Replace("sj", "s").Replace("kj", "s").Replace("tj", "s").Replace("hj", "j").Replace("hv", "v").Replace("ck", "k").Replace("aa", "o");
        var b = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s)
        {
            char c = ch switch { 'å' or 'ø' or 'ö' => 'o', 'æ' or 'ä' => 'e', 'á' => 'a', _ => ch };
            if (b.Length == 0 || b[^1] != c) b.Append(c);
        }
        if (b.Length > 2 && b[^1] == 'g') b[^1] = 'k';
        return b.ToString();
    }

    private static int Distance(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit) return limit + 1;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1], prev[j]) + 1, prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();
}
