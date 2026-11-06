using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FlightCompanion.Game;
using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.Career;
using FlightCompanion.Game.Flight;
using FlightCompanion.Game.Missions;
using FlightCompanion.Game.People;
using FlightCompanion.Game.Talk;
using FlightCompanion.Game.World;

namespace FlightCompanion.Services;

public sealed record RingInfo(Person Person, Mission? Mission, string? Reason, DateTimeOffset Since);

public sealed record ChatLine(DateTimeOffset When, string Who, string Text, bool FromPilot);

/// <summary>
/// The running game. All game state changes on one loop, posted from the sim, the mic, the UI and the network, so no two
/// threads ever change the career at once. The UI gets an event, then asks for a fresh picture.
/// </summary>
public sealed class GameHost : IDisposable
{
    private readonly Channel<Action> _loop = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Random _rng;
    private readonly Director _director;
    private readonly MissionFlow _flow;
    private readonly FlightMonitor _monitor = new();
    private readonly Dictionary<string, List<uint>> _spawned = [];
    private readonly Dictionary<string, WeatherPoint?> _forecast = [];
    private IReadOnlyList<string> _peopleTitles = [];
    private readonly List<ChatLine> _chat = [];

    private IDisposable? _ringTone;
    private RingInfo? _ringing;
    private Conversation? _talk;
    private Mission? _talkMission;
    private TalkMode _talkMode;
    private CancellationTokenSource? _scribeCts;
    private bool _endCallSoon;
    private DateTimeOffset _lastReaction = DateTimeOffset.MinValue;
    private DateTimeOffset _nextChatter = DateTimeOffset.MaxValue;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPilotWords = DateTimeOffset.MinValue;
    private Task? _voicesLoading;
    private string _partial = "";
    private float _micLevel;

    public CareerState Career { get; }
    public AppSettings Settings { get; }
    public KeyStore Keys { get; }
    public SimLink Sim { get; }
    public AudioOut Audio { get; }
    public Listener Mic { get; }
    public Brain Brain { get; }
    public ElevenLabs Voice { get; }
    public VoiceCaster Caster { get; }
    public ClipCache Clips { get; }
    public WeatherFeed Weather { get; } = new();

    public Telemetry? Latest { get; private set; }
    public Health SimHealth { get; private set; } = new(HealthLevel.Working, L.T("Waiting for the sim"));
    public Health MicHealth { get; private set; } = new(HealthLevel.Off, L.T("Mic off"));
    public Health AiHealth { get; private set; } = new(HealthLevel.Off, L.T("Not used yet"));
    public Health VoiceHealth { get; private set; } = new(HealthLevel.Off, L.T("Not used yet"));
    public Health OutHealth { get; private set; } = new(HealthLevel.Working, L.T("Opening audio"));
    public string? LastTiming { get; private set; }
    public RingInfo? Ringing => _ringing;
    public Person? TalkingTo => _talk?.Person;
    public TalkMode TalkMode => _talkMode;
    public Mission? TalkMission => _talkMission;
    public string Partial => _partial;
    public float MicLevel => _micLevel;
    public double Comfort => _monitor.Comfort;
    public IReadOnlyList<ChatLine> Chat => _chat;
    public Mission? ActiveMission => _flow.Active;
    public WeatherPoint? ForecastFor(Mission m) => _forecast.GetValueOrDefault(m.Id);

    /// <summary>Game state changed: redraw the structure. Raised on the game loop.</summary>
    public event Action? Changed;
    /// <summary>Only numbers moved: airspeed, mic level. Cheap and frequent.</summary>
    public event Action? LiveChanged;
    public event Action<string>? Toast;

    private readonly bool _offline;

    /// <param name="preview">Renders the screen from this career with no sim, mic, GPU or saving, for checking the layout.</param>
    public GameHost(CareerState? preview = null)
    {
        _offline = preview is not null;
        Settings = _offline ? new AppSettings() : AppSettings.Load();
        L.Norwegian = Settings.TalkLanguage != "en";
        Keys = new KeyStore();
        Career = preview ?? CareerStore.Load(AppPaths.Career);
        _rng = new Random(Career.Seed + Career.RandomCounter++);
        _director = new Director(Career, _rng);
        _flow = new MissionFlow(Career);

        Audio = new AudioOut { Volume = (float)Settings.VoiceVolume };
        Audio.HealthChanged += h => Post(() => { OutHealth = h; Changed?.Invoke(); });
        Sim = new SimLink(connect: !_offline);
        Sim.HealthChanged += h => Post(() => { SimHealth = h; Changed?.Invoke(); });
        Sim.Sample += t => Post(() => OnTelemetry(t));
        Mic = new Listener(Settings, load: !_offline) { Muted = Settings.MicMuted };
        LanguageChanged();
        Mic.HealthChanged += h => Post(() => { MicHealth = h; Changed?.Invoke(); });
        Mic.Level += l => { _micLevel = Math.Max(l, _micLevel * 0.8f); LiveChanged?.Invoke(); };
        Mic.PilotStartedTalking += () => Post(() => _talk?.PilotStartedTalking());
        Mic.Partial += p => Post(() => { _partial = p; Changed?.Invoke(); });
        Mic.Final += (text, lang) => Post(() => OnPilotSpoke(text, lang));
        Brain = new Brain(Keys, Settings);
        Voice = new ElevenLabs(Keys);
        Caster = new VoiceCaster(Voice);
        Clips = new ClipCache(Voice);

        _ = Task.Run(LoopAsync);
        if (_offline) return;
        _ = Task.Run(TickerAsync);
        _ = Task.Run(() => _peopleTitles = SimLink.FindPeopleTitles());
        LoadVoices();
    }

    /// <summary>For the layout preview only.</summary>
    public void PreviewSeed(Telemetry? t, RingInfo? ring, IEnumerable<ChatLine> chat)
    {
        Latest = t;
        _ringing = ring;
        _chat.AddRange(chat);
    }

    public void Post(Action a) => _loop.Writer.TryWrite(a);

    private async Task LoopAsync()
    {
        await foreach (var a in _loop.Reader.ReadAllAsync())
        {
            try { a(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log.Error("Game", "Loop action failed", ex); }
        }
    }

    private async Task TickerAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
        {
            Post(Tick);
            _ = Weather.RefreshMetarsAsync(_stop.Token);
        }
    }

    private void LoadVoices()
    {
        if (!Voice.HasKey || _voicesLoading is { IsCompleted: false }) return;
        _voicesLoading = Task.Run(async () =>
        {
            try
            {
                await Caster.RefreshAsync(_stop.Token);
                Post(() => { VoiceHealth = new Health(HealthLevel.Good, L.F("{0} voices available", Caster.Count)); Changed?.Invoke(); });
            }
            catch (VoiceException ex)
            {
                Log.Error("Voice", "Could not list voices", ex);
                Post(() => { VoiceHealth = new Health(HealthLevel.Problem, ex.KeyProblem ? L.T("The ElevenLabs key was refused. Add a working key in Settings.") : ex.Message); Changed?.Invoke(); });
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                Log.Error("Voice", "Could not reach ElevenLabs", ex);
                Post(() => { VoiceHealth = new Health(HealthLevel.Problem, L.T("Could not reach ElevenLabs")); Changed?.Invoke(); });
            }
        });
    }

    public void KeysChanged()
    {
        LoadVoices();
        Post(() => { AiHealth = new Health(Brain.HasKey ? HealthLevel.Good : HealthLevel.Off, L.T(Brain.HasKey ? "Key saved" : "No key")); Changed?.Invoke(); });
    }

    public bool ReadyForCalls => Brain.HasKey && Voice.HasKey;

    // ---- Clock -------------------------------------------------------------------------------------------------

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        _micLevel *= 0.8f;

        if (_ringing is { } r && (now - r.Since).TotalSeconds > 35) MissedCall(r);

        if (_talk is not null)
        {
            Mic.HalfDuplexHold = Settings.UsingSpeakers && _talk.IsSpeaking;
            if (_endCallSoon && !_talk.IsSpeaking) HangUp();
            else if (_talkMode == TalkMode.Intercom && now >= _nextChatter && !_talk.IsSpeaking && (now - _lastPilotWords).TotalSeconds > 25)
            {
                ScheduleChatter(now);
                _talk.React("Nothing is happening right now, it is a quiet moment in the flight. If it suits you, say one small thing about the view or ask something, otherwise only one or two words");
            }
        }

        if (ReadyForCalls && _ringing is null && _talk is null)
        {
            var action = _director.Tick(new DirectorContext
            {
                Now = now, InCall = false, Airborne = _monitor.Airborne,
                PassengersAboard = ActiveMission?.Phase is MissionPhase.Loaded or MissionPhase.Airborne or MissionPhase.Landed,
                DoNotDisturb = Settings.DoNotDisturb, Lat = Latest?.Lat, Lon = Latest?.Lon,
            });
            Apply(action, now);
        }

        if (Career.LastLoanPayment is null) Career.LastLoanPayment = now;
        else if ((now - Career.LastLoanPayment.Value).TotalDays >= 7)
        {
            Career.LastLoanPayment = now;
            Career.Book(now, L.T("Weekly payment: aircraft loan and hangar"), -3200);
            if (Career.LoanNok > 0) Career.LoanNok = Math.Max(0, Career.LoanNok - 2400);
            Changed?.Invoke();
        }

        if ((now - _lastSave).TotalSeconds > 30) Save();
    }

    private void Apply(DirectorAction? action, DateTimeOffset now)
    {
        switch (action)
        {
            case RingNewJob job when Career.PersonById(job.Mission.CallerId) is { } caller:
                StartRinging(new RingInfo(caller, job.Mission, null, now));
                _ = RefreshForecast(job.Mission);
                break;
            case RingAbout about:
                StartRinging(new RingInfo(about.Person, about.Mission, about.Reason, now));
                break;
            case TextMessage text:
                Career.Inbox.Add(new SmsMessage(now, text.Person.Name, text.Text));
                Audio.Beep(1320, 0.08);
                Toast?.Invoke(L.F("Text from {0}: {1}", text.Person.Name, text.Text));
                Changed?.Invoke();
                break;
        }
    }

    private void StartRinging(RingInfo r)
    {
        _ringing = r;
        _ringTone = Audio.Ring();
        Log.Info("Game", $"Ringing: {r.Person.Name}, {r.Mission?.Title ?? r.Reason}");
        Changed?.Invoke();
    }

    private void MissedCall(RingInfo r)
    {
        StopRing();
        var now = DateTimeOffset.Now;
        if (r.Mission is { Phase: MissionPhase.Ringing } m) m.Phase = MissionPhase.Cancelled;
        Career.Inbox.Add(new SmsMessage(now, r.Person.Name, L.T(r.Reason is null ? "Tried to call you about a flight. I will ask someone else." : "Tried to call you. Call me back.")));
        Changed?.Invoke();
    }

    private void StopRing()
    {
        _ringTone?.Dispose();
        _ringTone = null;
        _ringing = null;
    }

    private async Task RefreshForecast(Mission m)
    {
        if (m.Pickup is not { } place) return;
        var when = m.Plan.PickupTime ?? DateTimeOffset.Now;
        var f = await Weather.ForecastAsync(place, when, _stop.Token).ConfigureAwait(false);
        Post(() => { _forecast[m.Id] = f; Changed?.Invoke(); });
    }

    // ---- Calls -------------------------------------------------------------------------------------------------

    public void Answer() => Post(() =>
    {
        if (_ringing is not { } r) return;
        StopRing();
        if (r.Mission is { Phase: MissionPhase.Ringing } m) m.Phase = MissionPhase.Negotiating;
        Audio.Beep();
        OpenTalk(r.Person, r.Mission, TalkMode.Phone, r.Reason, r.Reason is null ? "The pilot just picked up the phone. Say hello." : "The pilot just picked up.");
    });

    public void Decline() => Post(() =>
    {
        if (_ringing is not { } r) return;
        StopRing();
        if (r.Mission is { Phase: MissionPhase.Ringing } m)
        {
            m.Phase = MissionPhase.Declined;
            r.Person.Trust = Math.Clamp(r.Person.Trust - 2, -100, 100);
        }
        Changed?.Invoke();
    });

    /// <summary>You call them, to say you are late, move a time or call it off because of weather.</summary>
    public void CallPerson(string missionId) => Post(() =>
    {
        if (_talk is not null || Career.MissionById(missionId) is not { } m || Career.PersonById(m.CallerId) is not { } p) return;
        Audio.Beep();
        OpenTalk(p, m, TalkMode.Phone, "The pilot is calling you about this flight. You do not know why yet, so answer the phone and find out.", "You just answered your phone. The pilot is calling. Answer with your name.");
    });

    /// <summary>Pinning recognition to the spoken language stops a short "ja" being heard as German.</summary>
    public void LanguageChanged() => Mic.PreferredLanguage = Settings.TalkLanguage switch { "no" => "no", "en" => "en", _ => null };

    public bool TestingMic { get; private set; }

    /// <summary>Listens without any call, and shows what it hears. Answers "can it hear me?" before it matters.</summary>
    public void ToggleMicTest() => Post(() =>
    {
        if (_talk is not null) return;
        TestingMic = !TestingMic;
        if (TestingMic)
        {
            _chat.Clear();
            AddChat(L.T("Mic test"), L.T("Say something. Your words should appear here."), false);
            Mic.Start();
        }
        else
        {
            Mic.Stop();
            _partial = "";
        }
        Changed?.Invoke();
    });

    public void RequestJob() => Post(() =>
    {
        if (_ringing is not null || _talk is not null || !ReadyForCalls) return;
        var m = _director.ForceNewJob(new DirectorContext { Now = DateTimeOffset.Now, Lat = Latest?.Lat, Lon = Latest?.Lon });
        if (m is not null && Career.PersonById(m.CallerId) is { } caller)
        {
            StartRinging(new RingInfo(caller, m, null, DateTimeOffset.Now));
            _ = RefreshForecast(m);
        }
    });

    private void OpenTalk(Person who, Mission? m, TalkMode mode, string? reason, string openingNote)
    {
        if (!ReadyForCalls)
        {
            Toast?.Invoke(L.T("Add your Claude and ElevenLabs keys in Settings first"));
            return;
        }
        string? voice = Caster.VoiceFor(who, Career.People.Where(p => p != who));
        if (voice is null)
        {
            VoiceHealth = new Health(HealthLevel.Problem, L.T("No voices available from ElevenLabs"));
            LoadVoices();
            Changed?.Invoke();
            return;
        }
        CloseTalkObject();
        TestingMic = false;
        _talkMission = m;
        _talkMode = mode;
        _endCallSoon = false;
        _chat.Clear();
        _partial = "";
        var fx = mode switch { TalkMode.Phone => VoiceFx.Phone, TalkMode.Intercom => VoiceFx.Intercom, _ => VoiceFx.Clear };
        var talk = new Conversation(Brain, Voice, Clips, Caster, Audio, who, voice, fx,
            Prompts.Speaker(who, m, Career, mode, reason, Settings.TalkLanguage), () => SituationNow(m), Settings.TalkLanguage);
        talk.PersonSaid += s => Post(() => AddChat(who.Name, s, false));
        talk.PilotSaid += s => Post(() => { _partial = ""; AddChat(Career.PilotName, s, true); });
        talk.ExchangeDone += () => Post(() => OnExchangeDone(talk));
        talk.AiHealth += h => Post(() => { AiHealth = h; Changed?.Invoke(); });
        talk.VoiceHealth += h => Post(() => { VoiceHealth = h; Changed?.Invoke(); });
        talk.Timing += t => Post(() => { LastTiming = t; Changed?.Invoke(); });
        _talk = talk;
        Mic.Start();
        PreloadClips(who, voice);
        talk.Open(openingNote);
        Changed?.Invoke();
    }

    private void PreloadClips(Person who, string voice)
    {
        string lang = SpeechText.VoiceLanguage(who.Language, who.SpeaksNorwegian, null, Settings.TalkLanguage);
        var ct = _stop.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var f in lang == "no" ? ReactionLines.FillersNo : ReactionLines.FillersEn)
                    await Clips.EnsureAsync(voice, lang, f, ct);
                if (_talkMode != TalkMode.Phone)
                    foreach (var (_, line) in ReactionLines.All(lang))
                        await Clips.EnsureAsync(voice, lang, line, ct);
            }
            catch (VoiceException ex) { Log.Error("Voice", "Could not prepare clips", ex); }
            catch (OperationCanceledException) { }
            catch (System.Net.Http.HttpRequestException ex) { Log.Error("Voice", "Could not prepare clips", ex); }
        }, ct);
    }

    private string SituationNow(Mission? m)
    {
        var now = DateTimeOffset.Now;
        string? phone = m is not null && _talkMode == TalkMode.Phone ? WeatherFeed.PhoneAppVersion(_forecast.GetValueOrDefault(m.Id)) : null;
        string? sight = Latest is { OnGround: false } t ? WorldData.NearestSight(t.Lat, t.Lon, 8)?.Name : null;
        return Prompts.Situation(now, m, Latest, _monitor.Comfort, phone, sight);
    }

    private void OnPilotSpoke(string text, string lang)
    {
        _lastPilotWords = DateTimeOffset.Now;
        _partial = "";
        if (TestingMic) AddChat(L.F("Heard you ({0})", lang), text, true);
        if (_talk is null) { Changed?.Invoke(); return; }
        _talkMission?.Transcript.Add($"{Career.PilotName}: {text}");
        _talk.OnPilotSpoke(text, lang);
        Changed?.Invoke();
    }

    private void AddChat(string who, string text, bool pilot)
    {
        _chat.Add(new ChatLine(DateTimeOffset.Now, who, text, pilot));
        if (!pilot) _talkMission?.Transcript.Add($"{who}: {text}");
        Changed?.Invoke();
    }

    /// <summary>Speech never waits for this. The newest exchange replaces a note taking still in progress.</summary>
    private void OnExchangeDone(Conversation talk)
    {
        if (_talk != talk || _talkMission is not { } m || _talkMode is TalkMode.Intercom) return;
        _scribeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scribeCts = cts;
        string prompt = Prompts.ScribeUser(DateTimeOffset.Now, m.Plan, talk.TranscriptLines(Career.PilotName).TakeLast(30));
        _ = Task.Run(async () =>
        {
            try
            {
                var patch = await Brain.ListenAsync(prompt, cts.Token).ConfigureAwait(false);
                if (patch is not null) Post(() => ApplyPatch(m, patch));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Error("Scribe", "Note taking failed", ex);
                Post(() => { AiHealth = new Health(HealthLevel.Problem, Brain.Explain(ex)); Changed?.Invoke(); });
            }
        });
    }

    private void ApplyPatch(Mission m, PlanPatch p)
    {
        var changes = PlanPatcher.Apply(m.Plan, p, DateTimeOffset.Now, Clock.Zone);
        if (changes.Any(c => c.Field is "time" or "pickup")) _ = RefreshForecast(m);
        if (changes.Any(c => c.Field == "time"))
        {
            m.LateCallMade = false;
            m.ReminderSent = false;
        }
        if (p.PilotDeclined == true && m.Phase == MissionPhase.Negotiating) m.Phase = MissionPhase.Declined;
        if (p.PilotDeclined == true && m.Phase == MissionPhase.Booked)
        {
            m.Phase = MissionPhase.Cancelled;
            if (Career.PersonById(m.CallerId) is { } who) who.Trust = Math.Clamp(who.Trust - 6, -100, 100);
            Toast?.Invoke(L.F("Cancelled: {0}", m.Title));
        }
        if (p.CallIsOver == true && _talkMode == TalkMode.Phone) _endCallSoon = true;
        Changed?.Invoke();
    }

    public void HangUp() => Post(HangUpNow);

    private void HangUpNow()
    {
        if (_talk is null) return;
        var m = _talkMission;
        var mode = _talkMode;
        var transcript = _talk.TranscriptLines(Career.PilotName).ToList();
        CloseTalkObject();
        Mic.Stop();
        Audio.Beep(520, 0.1);
        _talkMission = null;
        _endCallSoon = false;

        if (m is not null && mode == TalkMode.Phone && m.Phase == MissionPhase.Negotiating)
        {
            // One final pass over the whole call, so the booking reflects its last sentences even if the last note was cancelled.
            _ = Task.Run(async () =>
            {
                try
                {
                    var patch = await Brain.ListenAsync(Prompts.ScribeUser(DateTimeOffset.Now, m.Plan, transcript), _stop.Token).ConfigureAwait(false);
                    Post(() => { if (patch is not null) ApplyPatch(m, patch); FinishNegotiation(m); });
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log.Error("Scribe", "Final note failed", ex);
                    Post(() => FinishNegotiation(m));
                }
            });
        }
        Save();
        Changed?.Invoke();
    }

    private void FinishNegotiation(Mission m)
    {
        if (m.Phase == MissionPhase.Negotiating)
        {
            bool enough = m.Plan.Agreed || (m.Plan.PickupTime is not null && m.Plan.PriceNok is not null);
            if (enough)
            {
                m.Plan.PickupTime ??= DateTimeOffset.Now.AddMinutes(60);
                m.Plan.PriceNok ??= SuggestedPrice(m);
                m.Plan.Agreed = true;
                m.Phase = MissionPhase.Booked;
                Toast?.Invoke(L.F("Booked: {0}, {1} at {2}", m.Title, m.Pickup?.Name, Clock.ToLocal(m.Plan.PickupTime.Value).ToString("ddd HH:mm")));
            }
            else m.Phase = MissionPhase.Declined;
        }
        Save();
        Changed?.Invoke();
    }

    public double SuggestedPrice(Mission m)
    {
        var tpl = MissionCatalog.ById(m.TemplateId);
        var (_, fuel, _) = PlanEvaluator.Evaluate(m, AircraftProfile.ForTitle(Latest?.Title), Career.PilotKg);
        return Math.Round(Math.Max(tpl?.MinimumNok ?? 2500, fuel.TripHours * (tpl?.PricePerHourNok ?? 5200)) / 100) * 100;
    }

    private void CloseTalkObject()
    {
        _scribeCts?.Cancel();
        _talk?.Dispose();
        _talk = null;
        Mic.HalfDuplexHold = false;
    }

    // ---- At the strip ---------------------------------------------------------------------------------------------

    public void Weigh() => Post(() =>
    {
        if (ActiveMission is not { Phase: MissionPhase.AtPickup or MissionPhase.Boarding } m) return;
        double before = m.Plan.BaggageKg;
        int paxBefore = m.Plan.Passengers.Count;
        _flow.PutOnScale(m, DateTimeOffset.Now);
        Audio.Beep(990, 0.06);
        _talk?.React($"The pilot just put your bags on the scale in front of you. It reads {m.Plan.BaggageKg:0} kg in total, and they thought it was {before:0} kg. Everyone also stepped on the scale. There are {m.Plan.Passengers.Count} of you, and the pilot expected {paxBefore}");
        Changed?.Invoke();
    });

    /// <summary>Bags or cargo stay on the ground. The numbers only change here, by the pilot's hand.</summary>
    public void LeaveBehind(double kg) => Post(() =>
    {
        if (ActiveMission is not { Phase: MissionPhase.AtPickup or MissionPhase.Boarding } m) return;
        double fromBags = Math.Min(kg, m.Plan.BaggageKg);
        m.Plan.BaggageKg -= fromBags;
        m.Truth.BaggageKg = Math.Max(0, m.Truth.BaggageKg - fromBags);
        double fromCargo = Math.Min(kg - fromBags, m.Plan.CargoKg);
        m.Plan.CargoKg -= fromCargo;
        m.Truth.CargoKg = Math.Max(0, m.Truth.CargoKg - fromCargo);
        m.Score.BaggageLeftBehindKg += (int)Math.Round(kg);
        m.Plan.Changes.Add(new PlanChange(DateTimeOffset.Now, "left behind", "", $"{kg:0} kg"));
        Changed?.Invoke();
    });

    public void MeetInPerson() => Post(() =>
    {
        if (ActiveMission is not { } m || Career.PersonById(m.CallerId) is not { } p) return;
        if (m.Phase is not (MissionPhase.AtPickup or MissionPhase.Boarding)) return;
        if (_talk?.Person == p && _talkMode == TalkMode.InPerson) return;
        m.Phase = MissionPhase.Boarding;
        OpenTalk(p, m, TalkMode.InPerson, null, m.ArrivedAtPickup is { } a && m.Plan.PickupTime is { } due && (a - due).TotalMinutes > 15
            ? $"The pilot has just walked over to you, {(a - due).TotalMinutes:0} minutes late. Greet them, with that in mind."
            : "The pilot has just walked over to you. Greet them.");
    });

    public void Board() => Post(() =>
    {
        if (ActiveMission is not { Phase: MissionPhase.AtPickup or MissionPhase.Boarding } m) return;
        var a = AircraftProfile.ForTitle(Latest?.Title);
        // Skip the scale and what goes on board is whatever they really brought, which is the whole point of the scale.
        // After weighing, it is the deal made at the scale, including anything or anyone left behind.
        bool weighed = m.Plan.BaggageWeighed;
        var load = LoadPlanner.Compute(new LoadRequest
        {
            Aircraft = a, PilotKg = Career.PilotKg,
            PassengerKg = (weighed ? m.Plan.Passengers : m.Truth.Passengers).Select(p => p.Kg).ToList(),
            BaggageKg = weighed ? m.Plan.BaggageKg + m.Plan.CargoKg : m.Truth.BaggageKg + m.Truth.CargoKg,
            FuelGal = Latest?.FuelGal ?? 30,
        });
        var sim = Sim.Stations;
        if (Sim.Connected && sim.Count > 0)
        {
            Sim.SetStations(StationMapper.ToSim(sim, load.Stations));
            Log.Info("Game", $"Loaded {load.TakeoffLb:0} lb, stations: " + string.Join(", ", sim.Select(s => $"{s.SimIndex} {s.Name} = {s.Role}")));
        }
        _flow.CommitLoad(m, DateTimeOffset.Now);
        DespawnPeople(m);
        double fear = m.CompanionIds.Concat([m.CallerId]).Select(id => Career.PersonById(id)?.Fear ?? 0).DefaultIfEmpty(0).Max();
        _monitor.BeginCabin(fear);

        if (m.Plan.Passengers.Count > 0 && Career.PersonById(m.CallerId) is { } lead)
            OpenTalk(lead, m, TalkMode.Intercom, null, "You have just climbed in, the door is shut and you have put on your headset. Say something short.");
        else
        {
            CloseTalkObject();
            Mic.Stop();
            _talkMission = null;
        }
        ScheduleChatter(DateTimeOffset.Now);
        Save();
        Changed?.Invoke();
    });

    /// <summary>Everyone off, fare paid, memories written.</summary>
    public void Deplane() => Post(() =>
    {
        if (ActiveMission is not { Phase: MissionPhase.Landed } m) return;
        var now = DateTimeOffset.Now;
        var lead = Career.PersonById(m.CallerId);
        double price = m.Dropoff?.FuelPriceNokPerGal ?? 120;
        var review = ReviewMaker.Make(m, Career, lead, now, price);
        ReviewMaker.Settle(m, review, Career, now);
        ArcDirector.EpisodeFinished(Career, m, now);
        if (Sim.Connected && Sim.Stations.Count > 0) Sim.SetStations(StationMapper.Empty(Sim.Stations));
        if (Latest is { } t) { Career.LastLat = t.Lat; Career.LastLon = t.Lon; }

        var tpl = MissionCatalog.ById(m.TemplateId);
        if (tpl?.HasReturn == true)
        {
            var when = m.Plan.ReturnTime ?? new DateTimeOffset(now.AddDays(2).Date.AddHours(14), Clock.Zone.GetUtcOffset(now.AddDays(2).Date.AddHours(14)));
            var back = MissionFactory.CreateReturn(m, when, now);
            Career.Missions.Add(back);
            Career.Inbox.Add(new SmsMessage(now, lead?.Name ?? "Customer", L.F("See you at {0} on {1} at {2} for the trip home.", back.Pickup?.Name, Clock.ToLocal(when).ToString("dddd", L.Norwegian ? System.Globalization.CultureInfo.GetCultureInfo("nb-NO") : System.Globalization.CultureInfo.InvariantCulture), Clock.ToLocal(when).ToString("HH:mm"))));
        }
        if (review.Stars >= 4.5 && lead is not null && _rng.NextDouble() < 0.5)
        {
            var friend = CastFactory.Create(_rng, lead.Nationality);
            friend.ReferredBy = lead.Name;
            Career.People.Add(friend);
            var job = MissionFactory.Create(MissionCatalog.Templates[_rng.Next(MissionCatalog.Templates.Count)], new CareerView(Career), _rng, now, friend);
            Career.Missions.Add(job);
            Career.PendingCalls.Add(new PendingCall(now.AddMinutes(4 + _rng.NextDouble() * 12), friend.Id, $"{lead.Name} told you about this pilot and you want to arrange a flight. {job.Premise}", job.Id));
        }
        WriteMemories(m, review, lead);
        CloseTalkObject();
        Mic.Stop();
        _talkMission = null;
        _nextChatter = DateTimeOffset.MaxValue;
        Save();
        Changed?.Invoke();
    });

    private void WriteMemories(Mission m, FlightReview review, Person? lead)
    {
        if (lead is null || !Brain.HasKey) return;
        var tail = m.Transcript.TakeLast(14).ToList();
        string facts = Prompts.FlightFacts(m, review, tail);
        _ = Task.Run(async () =>
        {
            try
            {
                string memory = await Brain.OneLineAsync(Prompts.MemorySystem(lead), facts, _stop.Token);
                string quote = await Brain.OneLineAsync(Prompts.QuoteSystem(lead, Settings.TalkLanguage), facts, _stop.Token);
                Post(() =>
                {
                    lead.Memories.Add(new MemoryNote(DateTimeOffset.Now, memory));
                    review.Quote = quote;
                    Save();
                    Changed?.Invoke();
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Error("Game", "Could not write the memory", ex);
                Post(() => lead.Memories.Add(new MemoryNote(DateTimeOffset.Now, string.Join(", ", review.Points))));
            }
        });
    }

    /// <summary>Tops the tanks up to a target, from what this place sells or what you cached in the shed. Written as levels.</summary>
    public void Refuel(double targetGal) => Post(() =>
    {
        if (Latest is not { OnGround: true, EngineRunning: false } t || !Sim.Connected)
        {
            Toast?.Invoke(L.T("Land and shut down the engine first"));
            return;
        }
        var place = WorldData.Nearest(t.Lat, t.Lon, 2.5);
        if (place is null)
        {
            Toast?.Invoke(L.T("There is no fuel out here"));
            return;
        }
        double want = Math.Max(0, targetGal - t.FuelGal);
        double got = want;
        var now = DateTimeOffset.Now;
        if (place.FuelPriceNokPerGal is { } price)
        {
            Career.Book(now, L.F("Fuel at {0}: {1:0} gal", place.Name, got), -got * price);
        }
        else
        {
            double cache = Career.FuelCache.GetValueOrDefault(place.Id);
            got = Math.Min(want, cache);
            if (got <= 0)
            {
                Toast?.Invoke(L.F("No fuel cached at {0}. Fly some in.", place.Name));
                return;
            }
            Career.FuelCache[place.Id] = cache - got;
        }
        Sim.SetFuelTotalGal(t.FuelGal + got);
        Toast?.Invoke(L.F("Fuelled {0:0} gal at {1}", got, place.Name));
        Changed?.Invoke();
    });

    // ---- Flying ----------------------------------------------------------------------------------------------------

    private void OnTelemetry(Telemetry t)
    {
        Latest = t;
        Career.LastLat = t.Lat;
        Career.LastLon = t.Lon;
        var reserve = AircraftProfile.ForTitle(t.Title).BurnGph * 0.5;
        var flight = _monitor.Process(t, reserve);
        var now = DateTimeOffset.Now;
        var flow = _flow.Step(t, flight, now, _monitor.Comfort);
        foreach (var e in flow) OnFlow(e);
        foreach (var e in flight) OnFlightEvent(e, now);
        if (flow.Count > 0 || flight.Count > 0) Changed?.Invoke();
        LiveChanged?.Invoke();
    }

    private void OnFlow(FlowEvent e)
    {
        switch (e)
        {
            case PeopleShouldAppear p:
                SpawnPeople(p.Mission, p.Place);
                break;
            case ArrivedAtPickup a:
                Toast?.Invoke(a.LateMinutes > 15 ? L.F("You are {0:0} minutes late at {1}", a.LateMinutes, a.Mission.Pickup?.Name) : L.F("Arrived at {0}. Shut down and go meet them.", a.Mission.Pickup?.Name));
                break;
            case CanMeetInPerson m:
                if (Latest?.OnFoot == true) MeetInPerson();
                break;
            case MissionLanded l:
                if (_talk is not null && _talkMission == l.Mission && Career.PersonById(l.Mission.CallerId) is { } who)
                {
                    _talkMode = TalkMode.Debrief;
                    _talk.ChangeScene(Prompts.Speaker(who, l.Mission, Career, TalkMode.Debrief, null, Settings.TalkLanguage));
                }
                break;
            case ReadyToDeplane:
                Toast?.Invoke(L.T("Parked. Settle up with them on the card."));
                break;
        }
    }

    private void OnFlightEvent(FlightEvent e, DateTimeOffset now)
    {
        Log.Info("Flight", $"{e.Kind}: {e.Text}");
        if (_talk is null || _talkMode is not (TalkMode.Intercom or TalkMode.Debrief)) return;
        var who = _talk.Person;
        string lang = SpeechText.VoiceLanguage(who.Language, who.SpeaksNorwegian, Mic.LastLanguage, Settings.TalkLanguage);
        bool talkingNow = (now - _lastPilotWords).TotalSeconds < 3;

        var lines = ReactionLines.For(e.Kind, lang);
        if (lines.Count > 0 && !talkingNow) _talk.Say(lines[_rng.Next(lines.Count)]);

        bool worthWords = e.Kind is FlightEventKind.Landing or FlightEventKind.Takeoff or FlightEventKind.StallWarning or FlightEventKind.ExtremeBank
            or FlightEventKind.EngineStoppedInAir or FlightEventKind.LowFuel or FlightEventKind.DiveHard or FlightEventKind.Sightseeing;
        if (worthWords && !talkingNow && (now - _lastReaction).TotalSeconds > 15)
        {
            _lastReaction = now;
            _talk.React(e.Text);
        }
    }

    private void ScheduleChatter(DateTimeOffset now)
    {
        double chat = _talk?.Person.Chatty ?? 0.5;
        _nextChatter = now.AddSeconds(90 + (1 - chat) * 300 + _rng.NextDouble() * 120);
    }

    private void SpawnPeople(Mission m, Place place)
    {
        if (_peopleTitles.Count == 0 || _spawned.ContainsKey(m.Id)) return;
        var ids = new List<uint>();
        int n = m.Truth.Passengers.Count;
        for (int i = 0; i < n; i++)
        {
            var (lat, lon) = Geo.Offset(place.MeetingLat, place.MeetingLon, 60 + i * 40, 3 + i * 1.5);
            if (Sim.Spawn(_peopleTitles[_rng.Next(_peopleTitles.Count)], lat, lon, _rng.NextDouble() * 360) is { } id) ids.Add(id);
        }
        _spawned[m.Id] = ids;
    }

    private void DespawnPeople(Mission m)
    {
        if (!_spawned.Remove(m.Id, out var ids)) return;
        foreach (var id in ids) Sim.Remove(id);
    }

    // ---- Settings -----------------------------------------------------------------------------------------------

    public void SetMuted(bool muted)
    {
        Settings.MicMuted = muted;
        Mic.Muted = muted;
        Settings.Save();
        Post(() => Changed?.Invoke());
    }

    public void Save()
    {
        if (_offline) return;
        _lastSave = DateTimeOffset.Now;
        try { CareerStore.Save(Career, AppPaths.Career); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Log.Error("Game", "Could not save", ex); }
    }

    public void Dispose()
    {
        Save();
        _stop.Cancel();
        _loop.Writer.TryComplete();
        CloseTalkObject();
        _ringTone?.Dispose();
        Mic.Dispose();
        Sim.Dispose();
        Audio.Dispose();
    }
}
