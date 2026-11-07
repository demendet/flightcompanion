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

/// <summary>One person in a conversation: who they are, what they sound like, and the name they go by in the script.</summary>
public sealed record CastMember(Person Person, string VoiceId, Dialect Dialect, string Label);

public sealed class Conversation : IDisposable
{
    private readonly ISpeakerModel _brain;
    private readonly IVoiceStream _voice;
    private readonly ClipCache _clips;
    private readonly VoiceCaster _caster;
    private readonly AudioOut _audio;
    private readonly IReadOnlyList<CastMember> _cast;
    private readonly CastMember _lead;
    private readonly VoiceChannel _channel;
    private readonly Func<string> _situation;
    private readonly List<Turn> _history = [];
    private readonly object _lock = new();
    private string _system;
    private CancellationTokenSource? _turnCts;
    private Task _turnTask = Task.CompletedTask;
    private string? _pilotLanguage;
    private bool _ended;
    private readonly string _talkLanguage;

    /// <summary>Text only, for the on-screen conversation. Fires as each sentence starts.</summary>
    public event Action<string, string>? PersonSaid;
    public event Action<string>? PilotSaid;
    /// <summary>A whole exchange completed: the note taker can update the card now.</summary>
    public event Action? ExchangeDone;
    public event Action<Health>? AiHealth;
    public event Action<Health>? VoiceHealth;
    public event Action<string>? Timing;

    public Person Person => _lead.Person;
    public IReadOnlyList<CastMember> Cast => _cast;
    private bool Group => _cast.Count > 1;
    public IReadOnlyList<Turn> History { get { lock (_lock) return _history.ToList(); } }
    /// <summary>What was really said aloud, by both sides, without the notes the model gets with each turn.</summary>
    private readonly List<(bool Pilot, string Text)> _said = [];
    public IEnumerable<string> TranscriptLines(string pilotName)
    {
        lock (_lock) return _said.Select(t => (t.Pilot ? $"{pilotName}: " : Group ? "" : $"{_lead.Person.Name}: ") + t.Text).ToList();
    }
    public string? LastPilotWords { get { lock (_lock) return _said.LastOrDefault(t => t.Pilot).Text; } }
    /// <summary>They ended the call themselves.</summary>
    public event Action? HungUp;
    public bool HungUpOnPilot { get; private set; }
    private volatile bool _hanging;
    /// <summary>Only a phone call can be hung up on. In the aircraft the same words change nothing.</summary>
    public bool CanHangUp { get; set; }

    /// <summary>What the note taker has settled about this one talk. Its answer can arrive after the talk is over.</summary>
    public int OffenceLevel { get; set; }
    public bool Apologised { get; set; }
    public Game.Career.TalkRecord? Archived { get; set; }
    public CancellationTokenSource? NoteCts { get; set; }

    private string _lastSpoken = "";

    private string Speakable(string s)
    {
        var (text, hang) = SpeechText.TakeHangup(s);
        if (hang && CanHangUp) _hanging = true;
        string clean = SpeechText.Clean(text);
        if (clean.Length > 0) _lastSpoken = clean;
        return clean;
    }
    public bool IsSpeaking => _channel.IsPlaying;

    public Conversation(ISpeakerModel brain, IVoiceStream voice, ClipCache clips, VoiceCaster caster, AudioOut audio, IReadOnlyList<CastMember> cast, VoiceFx fx, string system, Func<string> situation, string talkLanguage)
    {
        _talkLanguage = talkLanguage;
        _brain = brain; _voice = voice; _clips = clips; _caster = caster; _audio = audio;
        _cast = cast; _lead = cast[0]; _system = system; _situation = situation;
        _fx = fx;
        _channel = audio.CreateVoice(fx);
    }

    private readonly VoiceFx _fx;
    private CancellationTokenSource? _panicCts;
    public bool Panicking => _panicCts is not null;

    /// <summary>
    /// Everyone screams at once, each on their own channel with their own clips, so nobody cuts anyone off. Nothing else talks
    /// until it is over, and the pilot talking over them does not stop it.
    /// </summary>
    public void StartPanic(Func<CastMember, string, IReadOnlyList<string>> linesFor)
    {
        if (_ended || _panicCts is not null) return;
        var loops = new List<(CastMember Who, List<byte[]> Clips)>();
        foreach (var member in _cast)
        {
            string lang = LanguageOf(member);
            var clips = linesFor(member, lang).Select(line => _clips.TryGet(member.VoiceId, lang, line)).OfType<byte[]>().ToList();
            if (clips.Count > 0) loops.Add((member, clips));
        }
        if (loops.Count == 0) return;   // nothing rendered yet: the ordinary reactions still run
        CancelTurn();
        var cts = new CancellationTokenSource();
        _panicCts = cts;
        foreach (var (who, clips) in loops)
        {
            PersonSaid?.Invoke(who.Person.Name, Game.L.T("(screaming)"));
            _ = Task.Run(() => PanicLoopAsync(clips, _audio.CreateVoice(_fx), loops.Count, cts.Token));
        }
    }

    public void StopPanic()
    {
        _panicCts?.Cancel();
        _panicCts = null;
    }

    private static async Task PanicLoopAsync(List<byte[]> clips, VoiceChannel channel, int people, CancellationToken ct)
    {
        const int BytesPerSecond = VoiceChannel.SampleRate * 2;
        try
        {
            await Task.Delay(Random.Shared.Next(0, 500), ct);
            int i = Random.Shared.Next(clips.Count);
            while (!ct.IsCancellationRequested)
            {
                byte[] clip = Quieter(clips[i++ % clips.Count], people);
                channel.Push(clip, clip.Length);
                await Task.Delay(clip.Length * 1000 / BytesPerSecond + Random.Shared.Next(50, 350), ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // The scream in progress is finished, not cut.
            for (int waited = 0; channel.IsPlaying && waited < 80; waited++) await Task.Delay(100, CancellationToken.None);
            channel.Dispose();
        }
    }

    /// <summary>Several people at full voice would clip the output.</summary>
    private static byte[] Quieter(byte[] pcm, int people)
    {
        if (people <= 1) return pcm;
        float gain = 1f / MathF.Sqrt(people);
        var quiet = new byte[pcm.Length];
        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            short s = (short)(BitConverter.ToInt16(pcm, i) * gain);
            quiet[i] = (byte)s;
            quiet[i + 1] = (byte)(s >> 8);
        }
        return quiet;
    }

    /// <summary>The scene changes without a new conversation: walking up to the plane, climbing in. People remember what was just said.</summary>
    public void ChangeScene(string system) => _system = system;

    /// <summary>The other person speaks first.</summary>
    public void Open(string firstNote) => StartTurn($"[{firstNote}]\n{_situation()}", null);

    public void PilotStartedTalking()
    {
        // They have already hung up: their goodbye plays out, and nothing the pilot says now reaches them.
        if (_hanging) return;
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

    private string LanguageOf(CastMember m) => SpeechText.VoiceLanguage(m.Person.Language, m.Person.SpeaksNorwegian, _pilotLanguage, _talkLanguage);

    /// <summary>Pre-rendered clip played straight through, from a random person on board: the reaction that beats the network.</summary>
    public void Say(Func<CastMember, string, string> lineFor)
    {
        if (_channel.IsPlaying || Panicking) return;
        var who = _cast[Random.Shared.Next(_cast.Count)];
        string lang = LanguageOf(who);
        var clip = _clips.TryGet(who.VoiceId, lang, lineFor(who, lang));
        if (clip is not null) _channel.Push(clip, clip.Length);
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
        if (_ended || Panicking || _hanging) return;
        CancelTurn();
        var cts = new CancellationTokenSource();
        lock (_lock) _turnCts = cts;
        var previous = _turnTask;
        _turnTask = Task.Run(async () =>
        {
            // The cancelled turn writes down what was heard first, so the history stays in order and always ends on
            // the newest thing said to them. The API refuses a history that ends on their own words.
            await previous.ConfigureAwait(false);
            lock (_lock)
            {
                _history.Add(new Turn(true, userContent));
                if (pilotText is not null) _said.Add((true, pilotText));
            }
            if (!cts.IsCancellationRequested) await RunTurnAsync(cts.Token).ConfigureAwait(false);
        });
    }

    private sealed record Spoken(CastMember Who, string Sentence, long EndsAtByte);

    private async Task RunTurnAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var sentences = Channel.CreateUnbounded<(CastMember Who, string Text)>();
        var spoken = new List<Spoken>();
        long firstByte = _channel.QueuedBytes;
        string lang = LanguageOf(_lead);
        bool firstAudio = false, firstText = false;

        var speakTask = Task.Run(async () =>
        {
            var previous = new Dictionary<CastMember, string>();
            CastMember? speaking = null;
            try
            {
                await foreach (var (who, s) in sentences.Reader.ReadAllAsync(ct))
                {
                    speaking = who;
                    PersonSaid?.Invoke(who.Person.Name, s);
                    await _voice.StreamAsync(s, who.VoiceId, LanguageOf(who), previous.GetValueOrDefault(who), (b, c) =>
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
                    lock (spoken) spoken.Add(new Spoken(who, s, _channel.QueuedBytes));
                    previous[who] = s;
                    VoiceHealth?.Invoke(new Health(HealthLevel.Good, Game.L.T("Voices working")));
                }
            }
            catch (OperationCanceledException) { }
            catch (VoiceException ex)
            {
                Log.Error("Talk", "Voice failed", ex);
                if (ex.VoiceNotAllowed && speaking is not null) _caster.Ban(speaking.VoiceId);
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
            var splitter = new SpeakerSplitter(_cast.Select(m => m.Label).ToList());
            CastMember ByLabel(string label) => _cast.First(m => m.Label == label);
            await foreach (var delta in _brain.SpeakAsync(_system, History, ct))
            {
                if (!firstText)
                {
                    firstText = true;
                    Log.Info("Talk", $"First words from Claude after {sw.ElapsedMilliseconds} ms");
                }
                whole.Append(delta);
                foreach (var (who, s) in splitter.Push(delta))
                {
                    string clean = Speakable(s);
                    if (clean.Length > 0) await sentences.Writer.WriteAsync((ByLabel(who), clean), ct);
                }
            }
            foreach (var (who, s) in splitter.Flush())
                if (Speakable(s) is { Length: > 0 } last) await sentences.Writer.WriteAsync((ByLabel(who), last), ct);
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
            if (heard.Length > 0)
            {
                _history.Add(new Turn(false, heard));
                _said.Add((false, heard));
            }
        }
        Log.Info("Talk", Group ? heard.Replace("\n", " / ") : $"{_lead.Person.Name}: {heard}");
        if (!ct.IsCancellationRequested) ExchangeDone?.Invoke();
        else if (heard.Length > 0) ExchangeDone?.Invoke();
        if (_hanging && !SpeechText.ReplyEndsCall(_lastSpoken))
        {
            Log.Info("Talk", "The line stays open: the reply ended on a question");
            _hanging = false;
        }
        if (_hanging && !HungUpOnPilot)
        {
            HungUpOnPilot = true;
            HungUp?.Invoke();
        }
    }

    /// <summary>In a group the history keeps the names, so everyone remembers who said what.</summary>
    private string HeardText(List<Spoken> spoken, long firstByte, bool cancelled)
    {
        List<Spoken> list;
        lock (spoken) list = spoken.ToList();
        var heardLines = new List<(CastMember Who, string Text)>();
        long heard = _channel.HeardBytes;
        long start = firstByte;
        foreach (var s in list)
        {
            if (!cancelled || heard >= s.EndsAtByte) heardLines.Add((s.Who, s.Sentence));
            else
            {
                double fraction = s.EndsAtByte > start ? (double)(heard - start) / (s.EndsAtByte - start) : 0;
                string part = SpeechText.HeardPart(s.Sentence, fraction);
                if (part.Length > 0) heardLines.Add((s.Who, part));
                break;
            }
            start = s.EndsAtByte;
        }
        if (!Group) return string.Join(" ", heardLines.Select(l => l.Text));
        var sb = new System.Text.StringBuilder();
        CastMember? last = null;
        foreach (var (who, text) in heardLines)
        {
            if (who != last) { if (last is not null) sb.Append('\n'); sb.Append(who.Label).Append(": ").Append(text); }
            else sb.Append(' ').Append(text);
            last = who;
        }
        return sb.ToString();
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
