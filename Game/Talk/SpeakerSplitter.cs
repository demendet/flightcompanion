using System.Text;

namespace FlightCompanion.Game.Talk;

/// <summary>
/// Reads a streaming reply where several people talk, written as "Kari: ..." on each line, and hands out finished sentences
/// with who said them. Times like "14:00" and unknown labels never end up read aloud as a name.
/// </summary>
public sealed class SpeakerSplitter
{
    private const int MaxLabel = 28;
    private readonly IReadOnlyList<string> _labels;
    private readonly StringBuilder _hold = new();
    private SentenceSplitter _sentences = new();
    private string _current;
    private bool _atLineStart = true;

    public SpeakerSplitter(IReadOnlyList<string> labels)
    {
        _labels = labels;
        _current = labels[0];
    }

    public IEnumerable<(string Who, string Sentence)> Push(string delta)
    {
        var found = new List<(string, string)>();
        foreach (char c in delta)
        {
            if (_atLineStart)
            {
                if (_hold.Length == 0 && char.IsWhiteSpace(c)) continue;
                if (c == ':') { ResolveLabel(found); continue; }
                _hold.Append(c);
                if (c == '\n' || char.IsDigit(c) || c is '.' or '!' or '?' or ',' || _hold.Length > MaxLabel) ReleaseHold(found);
                continue;
            }
            if (c == '\n')
            {
                FlushSentences(found);
                _atLineStart = true;
                continue;
            }
            Feed(c.ToString(), found);
        }
        return found;
    }

    public IEnumerable<(string Who, string Sentence)> Flush()
    {
        var found = new List<(string, string)>();
        if (_hold.Length > 0) ReleaseHold(found);
        FlushSentences(found);
        return found;
    }

    private void ResolveLabel(List<(string, string)> found)
    {
        string label = _hold.ToString().Trim();
        _hold.Clear();
        _atLineStart = false;
        string? match = _labels.FirstOrDefault(l => l.Equals(label, StringComparison.OrdinalIgnoreCase))
            ?? _labels.FirstOrDefault(l => l.Split(' ')[0].Equals(label.Split(' ')[0], StringComparison.OrdinalIgnoreCase));
        if (match is not null && match != _current)
        {
            FlushSentences(found);
            _current = match;
        }
        // An unknown label is dropped too: a voice should never read out a stranger's name and a colon.
    }

    private void ReleaseHold(List<(string, string)> found)
    {
        string text = _hold.ToString();
        _hold.Clear();
        _atLineStart = text.EndsWith('\n');
        if (_atLineStart) { Feed(text.TrimEnd('\n'), found); FlushSentences(found); }
        else Feed(text, found);
    }

    private void Feed(string text, List<(string, string)> found)
    {
        foreach (var s in _sentences.Push(text)) found.Add((_current, s));
    }

    private void FlushSentences(List<(string, string)> found)
    {
        if (_sentences.Flush() is { } rest) found.Add((_current, rest));
        _sentences = new SentenceSplitter();
    }
}
