using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FlightCompanion.Game.People;
using FlightCompanion.Game.Talk;

namespace FlightCompanion.Services;

/// <summary>
/// One live conversation. The whole reply path streams, sentence by sentence, and every turn has its own cancel token,
/// so talking over someone stops the voice, the AI and the audio generation together. What goes in the history is only
/// what the pilot actually heard, because a person cannot remember telling you something you talked over.
/// </summary>
public interface ISpeakerModel
{
    IAsyncEnumerable<string> SpeakAsync(string system, IReadOnlyList<Turn> history, CancellationToken ct);
}

public interface IVoiceStream
{
    Task StreamAsync(string text, string voiceId, string language, string? previousText, Action<byte[], int> onPcm, CancellationToken ct);
}

public sealed class Conversation : IDisposable
{
    private readonly ISpeakerModel _brain;
    private readonly IVoiceStream _voice;
    private readonly ClipCache _clips;
    private readonly VoiceCaster _caster;
    private readonly AudioOut _audio;
    private readonly Person _person;
    private readonly VoiceChannel _channel;
    private readonly Func<string> _situation;
    private readonly List<Turn> _history = [];
    private readonly object _lock = new();
    private string _system;
    private string _voiceId;
    private CancellationTokenSource? _turnCts;
    private Task _turnTask = Task.CompletedTask;
    private string? _pilotLanguage;
    private bool _ended;
    private readonly string _talkLanguage;

    /// <summary>Text only, for the on-screen conversation. Fires as each sentence starts.</summary>
    public event Action<string>? PersonSaid;
    public event Action<string>? PilotSaid;
    /// <summary>A whole exchange completed: the note taker can update the card now.</summary>
    public event Action? ExchangeDone;
    public event Action<Health>? AiHealth;
    public event Action<Health>? VoiceHealth;
    public event Action<string>? Timing;

    public Person Person => _person;
    public IReadOnlyList<Turn> History { get { lock (_lock) return _history.ToList(); } }
    public IEnumerable<string> TranscriptLines(string pilotName)
    {
        lock (_lock) return _history.Where(t => !t.Text.StartsWith('[')).Select(t => (t.FromPilot ? $"{pilotName}: " : $"{_person.Name}: ") + t.Text).ToList();
    }
    public bool IsSpeaking => _channel.IsPlaying;

    public Conversation(ISpeakerModel brain, IVoiceStream voice, ClipCache clips, VoiceCaster caster, AudioOut audio, Person person, string voiceId, VoiceFx fx, string system, Func<string> situation, string talkLanguage)
    {
        _talkLanguage = talkLanguage;
        _brain = brain; _voice = voice; _clips = clips; _caster = caster; _audio = audio;
        _person = person; _voiceId = voiceId; _system = system; _situation = situation;
        _channel = audio.CreateVoice(fx);
    }

    /// <summary>The scene changes without a new conversation: walking up to the plane, climbing in. People remember what was just said.</summary>
    public void ChangeScene(string system) => _system = system;

    /// <summary>The other person speaks first.</summary>
    public void Open(string firstNote) => StartTurn($"[{firstNote}]\n{_situation()}", null);

    public void PilotStartedTalking()
    {
        // Talking over someone: everything stops now, not when the sentence ends.
        CancelTurn();
    }

    public void OnPilotSpoke(string text, string language)
    {
        _pilotLanguage = language;
        PilotSaid?.Invoke(text);
        StartTurn($"[{_situation()}]\n{text}", text);
    }

    /// <summary>Something just happened in flight. The person reacts, unless the pilot is in the middle of a sentence.</summary>
    public void React(string whatHappened) => StartTurn($"[what just happened: {whatHappened}. {_situation()}]", null);

    public void Say(string line)
    {
        // Pre-rendered clip played straight through: this is the reaction that beats the network.
        var clip = _clips.TryGet(_voiceId, SpeechText.VoiceLanguage(_person.Language, _person.SpeaksNorwegian, _pilotLanguage, _talkLanguage), line);
        if (clip is not null && !_channel.IsPlaying) _channel.Push(clip, clip.Length);
    }

    private void CancelTurn()
    {
        CancellationTokenSource? cts;
        lock (_lock) { cts = _turnCts; _turnCts = null; }
        cts?.Cancel();
        _channel.Stop();
    }

    private void StartTurn(string userContent, string? pilotText)
    {
        if (_ended) return;
        CancelTurn();
        var cts = new CancellationTokenSource();
        lock (_lock) _turnCts = cts;
        var previous = _turnTask;
        _turnTask = Task.Run(async () =>
        {
            // The cancelled turn writes down what was heard first, so the history stays in order and always ends on
            // the newest thing said to them. The API refuses a history that ends on their own words.
            await previous.ConfigureAwait(false);
            lock (_lock) _history.Add(new Turn(true, userContent));
            if (!cts.IsCancellationRequested) await RunTurnAsync(cts.Token).ConfigureAwait(false);
        });
    }

    private sealed record Spoken(string Sentence, long EndsAtByte);

    private async Task RunTurnAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var sentences = Channel.CreateUnbounded<string>();
        var spoken = new List<Spoken>();
        long firstByte = _channel.QueuedBytes;
        string lang = SpeechText.VoiceLanguage(_person.Language, _person.SpeaksNorwegian, _pilotLanguage, _talkLanguage);
        bool firstAudio = false, firstText = false;

        // The filler goes out only if the real reply is slow, so most turns never use one.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(800, ct);
                if (!firstAudio && !_channel.IsPlaying)
                {
                    var fillers = lang == "no" ? ReactionLines.FillersNo : ReactionLines.FillersEn;
                    var clip = _clips.TryGet(_voiceId, lang, fillers[Random.Shared.Next(fillers.Length)]);
                    if (clip is not null) _channel.Push(clip, clip.Length);
                }
            }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);

        var speakTask = Task.Run(async () =>
        {
            string? previous = null;
            try
            {
                await foreach (var s in sentences.Reader.ReadAllAsync(ct))
                {
                    PersonSaid?.Invoke(s);
                    await _voice.StreamAsync(s, _voiceId, lang, previous, (b, c) =>
                    {
                        if (ct.IsCancellationRequested) return;
                        if (!firstAudio)
                        {
                            firstAudio = true;
                            Timing?.Invoke($"first audio {sw.ElapsedMilliseconds} ms");
                            Log.Info("Talk", $"First audio {sw.ElapsedMilliseconds} ms after the pilot finished");
                        }
                        _channel.Push(b, c);
                    }, ct);
                    lock (spoken) spoken.Add(new Spoken(s, _channel.QueuedBytes));
                    previous = s;
                    VoiceHealth?.Invoke(new Health(HealthLevel.Good, Game.L.T("Voices working")));
                }
            }
            catch (OperationCanceledException) { }
            catch (VoiceException ex)
            {
                Log.Error("Talk", "Voice failed", ex);
                if (ex.VoiceNotAllowed) _caster.Ban(_voiceId);
                VoiceHealth?.Invoke(new Health(HealthLevel.Problem, ex.KeyProblem ? Game.L.T("The ElevenLabs key was refused. Check it in Settings.")
                    : ex.VoiceNotAllowed ? Game.L.T("That voice needs a paid ElevenLabs plan, the next call will use another")
                    : ex.OutOfCredits ? Game.L.T("ElevenLabs credits have run out") : ex.Message));
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                Log.Error("Talk", "Voice network failure", ex);
                VoiceHealth?.Invoke(new Health(HealthLevel.Problem, Game.L.T("Could not reach ElevenLabs")));
            }
        }, CancellationToken.None);

        var whole = new System.Text.StringBuilder();
        try
        {
            var splitter = new SentenceSplitter();
            await foreach (var delta in _brain.SpeakAsync(_system, History, ct))
            {
                if (!firstText)
                {
                    firstText = true;
                    Log.Info("Talk", $"First words from Claude after {sw.ElapsedMilliseconds} ms");
                }
                whole.Append(delta);
                foreach (var s in splitter.Push(delta))
                {
                    string clean = SpeechText.Clean(s);
                    if (clean.Length > 0) await sentences.Writer.WriteAsync(clean, ct);
                }
            }
            string? rest = splitter.Flush();
            if (rest is not null && SpeechText.Clean(rest) is { Length: > 0 } last) await sentences.Writer.WriteAsync(last, ct);
            AiHealth?.Invoke(new Health(HealthLevel.Good, Game.L.T("Claude working")));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Error("Talk", "Claude failed", ex);
            AiHealth?.Invoke(new Health(HealthLevel.Problem, Brain.Explain(ex)));
        }
        finally
        {
            sentences.Writer.TryComplete();
        }
        await speakTask.ConfigureAwait(false);

        // Wait for the voice to finish or be cut, then record what was truly heard.
        try
        {
            while (!ct.IsCancellationRequested && _channel.IsPlaying) await Task.Delay(40, ct);
        }
        catch (OperationCanceledException) { }

        string heard = HeardText(spoken, firstByte, ct.IsCancellationRequested);
        lock (_lock)
        {
            // An empty assistant turn would be rejected on the next request, and nothing was said anyway.
            if (heard.Length > 0) _history.Add(new Turn(false, heard));
        }
        Log.Info("Talk", $"{_person.Name}: {heard}");
        if (!ct.IsCancellationRequested) ExchangeDone?.Invoke();
        else if (heard.Length > 0) ExchangeDone?.Invoke();
    }

    private string HeardText(List<Spoken> spoken, long firstByte, bool cancelled)
    {
        List<Spoken> list;
        lock (spoken) list = spoken.ToList();
        if (!cancelled) return string.Join(" ", list.Select(s => s.Sentence));
        long heard = _channel.HeardBytes;
        var parts = new List<string>();
        long start = firstByte;
        foreach (var s in list)
        {
            if (heard >= s.EndsAtByte) parts.Add(s.Sentence);
            else
            {
                double fraction = s.EndsAtByte > start ? (double)(heard - start) / (s.EndsAtByte - start) : 0;
                string part = SpeechText.HeardPart(s.Sentence, fraction);
                if (part.Length > 0) parts.Add(part);
                break;
            }
            start = s.EndsAtByte;
        }
        return string.Join(" ", parts);
    }

    public void End()
    {
        _ended = true;
        CancelTurn();
    }

    public void Dispose()
    {
        End();
        _channel.Dispose();
    }
}
