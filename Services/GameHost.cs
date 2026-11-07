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
    private readonly MapKnowledge _map = new();
    private readonly SightWatcher _sights = new();
    private DateTimeOffset _lastSightComment = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSightCheck = DateTimeOffset.MinValue;
    private string? _approachCalledFor;

    public Telemetry? Latest { get; private set; }
    public Health SimHealth { get; private set; } = new(HealthLevel.Working, L.T("Waiting for the sim"));
    public Health MicHealth { get; private set; } = new(HealthLevel.Off, L.T("Mic off"));
    public Health AiHealth { get; private set; } = new(HealthLevel.Off, L.T("Not used yet"));
    public Health VoiceHealth { get; private set; } = new(HealthLevel.Off, L.T("Not used yet"));
    public Health OutHealth { get; private set; } = new(HealthLevel.Working, L.T("Opening audio"));
    public string? LastTiming { get; private set; }
    public RingInfo? Ringing => _ringing;
    public Person? TalkingTo => _talk?.Person;
    public IReadOnlyList<string> TalkGroup => _talk?.Cast.Select(c => c.Label).ToList() ?? [];
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
        CastFactory.LocalsOnly = Settings.TalkLanguage == "no";
        if (CastFactory.LocalsOnly && !_offline) RenameForeignStrangers();
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
        Sim.Crashed += () => Post(() => { _panic.Reset(); OnCrash(); });
        Voice = new ElevenLabs(Keys, Settings);
        Caster = new VoiceCaster(Voice);
        Clips = new ClipCache(Voice, Settings);

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

    /// <summary>The player's own clone belongs to one person, who then keeps calling. A northern local fits the accent it was made with.</summary>
    private void GiveCloneAPerson()
    {
        if (Caster.ClonedVoiceId is not { } clone) return;
        var who = Career.People.FirstOrDefault(p => p.VoiceId == clone);
        if (who is null)
        {
            who = CastFactory.Create(_rng, "Norwegian", null, "Finnmark");
            who.Gender = "male";
            who.Name = (new[] { "Odd", "Terje", "Roger", "Kjell", "Arne", "Svein", "Ronny" })[_rng.Next(7)] + " " + who.Name.Split(' ')[^1];
            who.VoiceId = clone;
            Career.People.Add(who);
            Log.Info("Voice", $"{who.Name} now speaks with the cloned voice");
        }
        Career.FavoriteCallerId = who.Id;
    }

    private void RenameForeignStrangers()
    {
        foreach (var p in Career.People) CastFactory.MakeLocal(p, _rng);
        // People from calls made before talks were counted.
        foreach (var m in Career.Missions.Where(x => x.Transcript.Count >= 2))
            if (Career.PersonById(m.CallerId) is { Talks: 0 } caller) { caller.Talks = 1; caller.LastContact ??= m.CreatedAt; }
        foreach (var m in Career.Missions)
            foreach (var pax in m.Plan.Passengers.Concat(m.Truth.Passengers))
                if (Career.PersonById(pax.PersonId) is { } who) pax.Name = who.Name;
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
                Post(() => { GiveCloneAPerson(); VoiceHealth = new Health(HealthLevel.Good, L.F("{0} voices available", Caster.Count)); Changed?.Invoke(); });
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

    /// <summary>Settings button: puts real Norwegian voices on the ElevenLabs account. Needs a paid plan, costs no credits.</summary>
    public void AddNorwegianVoices()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var added = await Voice.AddNorwegianVoicesAsync(4, _stop.Token);
                await Caster.RefreshAsync(_stop.Token);
                Post(() =>
                {
                    Toast?.Invoke(L.F("Added {0} Norwegian voices", added.Count));
                    VoiceHealth = new Health(HealthLevel.Good, L.F("{0} voices available", Caster.Count));
                    Log.Info("Voice", "Added: " + string.Join(", ", added));
                    Changed?.Invoke();
                });
            }
            catch (VoiceException ex)
            {
                Log.Error("Voice", "Could not add Norwegian voices", ex);
                Post(() => Toast?.Invoke(L.T("Norwegian voices need a paid ElevenLabs plan")));
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                Log.Error("Voice", "Could not reach ElevenLabs", ex);
                Post(() => Toast?.Invoke(L.T("Could not reach ElevenLabs")));
            }
        });
    }

    /// <summary>
    /// The book numbers, corrected by the aircraft actually loaded: its empty weight is what is left after taking away
    /// what the sim says is in the seats and tanks. The first live read showed about 95 lb less than the book.
    /// </summary>
    public AircraftProfile CurrentAircraft()
    {
        var book = AircraftProfile.ForTitle(Latest?.Title);
        if (Latest is not { TotalWeightLb: > 0 } t || Sim.StationPounds.Length == 0) return book;
        double empty = t.TotalWeightLb - Sim.StationPounds.Sum() - t.FuelGal * book.FuelLbPerGal;
        return empty > book.EmptyWeightLb * 0.7 && empty < book.EmptyWeightLb * 1.3 ? book with { EmptyWeightLb = empty } : book;
    }

    /// <summary>The sim's pilot station when it has one. It reads 0 while your character is outside, then your set weight counts.</summary>
    public double PilotKgNow()
    {
        var stations = Sim.Stations;
        var pounds = Sim.StationPounds;
        var pilot = stations.FirstOrDefault(s => s.Role == StationRole.Pilot);
        if (pilot is not null && pilot.SimIndex <= pounds.Length && pounds[pilot.SimIndex - 1] > 40) return Units.LbToKg(pounds[pilot.SimIndex - 1]);
        return Career.PilotKg;
    }

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
            else if (_talkMode == TalkMode.Intercom && !OnBoardWithHarasser && now >= _nextChatter && _chatterLeft > 0 && !_talk.IsSpeaking && (now - _lastPilotWords).TotalSeconds > 25)
            {
                _chatterLeft--;
                ScheduleChatter(now);
                _talk.React(_talk.Cast.Count > 1
                    ? "Nothing is happening right now, it is a quiet moment in the flight. Say a small thing to each other or to the pilot about the view, one or two short lines"
                    : "Nothing is happening right now, it is a quiet moment in the flight. If it suits you, say one small thing about the view or ask something, otherwise only one or two words");
            }
            else if (_talkMode == TalkMode.InPerson && _talk.Cast.Count > 1 && now >= _nextChatter && _chatterLeft > 0 && !_talk.IsSpeaking && (now - _lastPilotWords).TotalSeconds > 15)
            {
                _chatterLeft--;
                _nextChatter = now.AddSeconds(25 + _rng.NextDouble() * 25);
                _talk.React("The pilot is busy and not talking to you right now. You are standing here waiting with your bags. Talk a little among yourselves about the trip, the bags, the cold or the pilot, one or two short lines");
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

    /// <summary>Ring someone from the people list, with or without a flight to talk about.</summary>
    public void CallPersonById(string personId) => Post(() =>
    {
        if (_talk is not null || _ringing is not null || Career.PersonById(personId) is not { } p) return;
        var open = Career.OpenMissions.FirstOrDefault(m => m.CallerId == p.Id);
        Audio.Beep();
        OpenTalk(p, open, TalkMode.Phone, open is null ? "The pilot is calling you, and you do not know why. Answer and find out. There is no flight booked between you right now." : "The pilot is calling you about your flight. You do not know why yet, so answer and find out.",
            "You just answered your phone. The pilot is calling. Answer with your name.");
    });

    /// <summary>Your own trip: who, what, from where to where. Comes back as a code you can send to someone else.</summary>
    public void MakeTrip(string? personId, string templateId, string fromId, string toId, bool startNow, Action<string> gotCode) => Post(() =>
    {
        if (MissionCatalog.ById(templateId) is not { } tpl) return;
        int seed = _rng.Next();
        var caller = Career.PersonById(personId) ?? TripCode.NewCaller(tpl, new Random(seed));
        string code = TripCode.Encode(new TripChoice { TemplateId = templateId, FromId = fromId, ToId = toId, Seed = seed, Caller = caller });
        gotCode(code);
        if (startNow) StartTripNow(code);
    });

    public void StartTrip(string code) => Post(() => StartTripNow(code));

    private void StartTripNow(string code)
    {
        if (TripCode.Decode(code) is not { } trip) { Toast?.Invoke(L.T("That trip code does not work")); return; }
        if (_ringing is not null || _talk is not null) { Toast?.Invoke(L.T("Finish the call you are in first")); return; }
        if (!ReadyForCalls) { Toast?.Invoke(L.T("Add your Claude and ElevenLabs keys in Settings first")); return; }
        var now = DateTimeOffset.Now;
        var m = TripCode.Start(trip, Career, now);
        if (Career.PersonById(m.CallerId) is not { } caller) return;
        if (caller.VoiceId is not null && caller.VoiceId == Caster.ClonedVoiceId) Career.FavoriteCallerId = caller.Id;
        StartRinging(new RingInfo(caller, m, null, now));
        _ = RefreshForecast(m);
        Save();
        Changed?.Invoke();
    }

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
        var cast = BuildCast(who, m, mode);
        Mic.Names = new NameFixer(WorldData.Places.Select(x => x.Name).Concat(WorldData.Landmarks.Select(x => x.Name)).Concat(cast.Select(x => x.Person.Name)).Concat(m?.Stops.Select(x => x.Name) ?? []));
        if (cast.Count == 0)
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
        _offenceThisTalk = 0;
        _apologisedThisTalk = false;
        _chat.Clear();
        _partial = "";
        var fx = !Settings.RadioEffect ? VoiceFx.Clear : mode switch { TalkMode.Phone => VoiceFx.Phone, TalkMode.Intercom => VoiceFx.Intercom, _ => VoiceFx.Clear };
        var talk = new Conversation(Brain, Voice, Clips, Caster, Audio, cast, fx, SystemFor(cast, m, mode, reason), () => SituationNow(m), Settings.TalkLanguage);
        talk.CanHangUp = mode == TalkMode.Phone;
        talk.PersonSaid += (name, s) => Post(() => AddChat(name, s, false));
        talk.PilotSaid += s => Post(() => { _partial = ""; AddChat(Career.PilotName, s, true); });
        talk.ExchangeDone += () => Post(() => OnExchangeDone(talk));
        talk.HungUp += () => Post(() =>
        {
            if (_talk != talk || _talkMode != TalkMode.Phone) return;
            _endCallSoon = true;
            Log.Info("Talk", $"{talk.Person.Name} hung up");
        });
        talk.AiHealth += h => Post(() => { AiHealth = h; Changed?.Invoke(); });
        talk.VoiceHealth += h => Post(() => { VoiceHealth = h; Changed?.Invoke(); });
        talk.Timing += t => Post(() => { LastTiming = t; Changed?.Invoke(); });
        _talk = talk;
        Mic.Start();
        PreloadClips(cast);
        _chatterLeft = 8;
        if (mode == TalkMode.InPerson && cast.Count > 1) _nextChatter = DateTimeOffset.Now.AddSeconds(20 + _rng.NextDouble() * 15);
        talk.Open(openingNote);
        Changed?.Invoke();
    }

    private int _chatterLeft;

    /// <summary>
    /// Everyone taking part. A phone call is one person. At the strip and in the air it is everyone who came, each with their own
    /// voice, including a friend who turned up unannounced, who becomes a real person the moment they are standing there.
    /// </summary>
    private List<CastMember> BuildCast(Person lead, Mission? m, TalkMode mode)
    {
        var people = new List<Person> { lead };
        if (m is not null && mode != TalkMode.Phone)
        {
            var aboard = m.Plan.BaggageWeighed ? m.Plan.Passengers : m.Truth.Passengers;
            foreach (var pax in aboard)
            {
                if (pax.PersonId is null)
                {
                    var friend = CastFactory.Companion(_rng, lead);
                    friend.WeightKg = pax.Kg;
                    Career.People.Add(friend);
                    pax.PersonId = friend.Id;
                    pax.Name = friend.Name;
                }
                if (Career.PersonById(pax.PersonId) is { } p && !people.Contains(p)) people.Add(p);
            }
        }
        var cast = new List<CastMember>();
        foreach (var p in people)
        {
            // Others in this same group count as taken first of all, so two people in one aircraft do not share a voice.
            string? voice = Caster.VoiceFor(p, cast.Select(c => c.Person).Concat(Career.People.Where(x => x != p && !people.Contains(x))));
            if (voice is null) continue;
            string first = p.Name.Split(' ')[0];
            string label = people.Count(x => x.Name.Split(' ')[0] == first) > 1 ? p.Name : first;
            // The player's own clone sounds right on plain words. Dialect spelling makes it sound like someone else.
            var dialect = voice == Caster.ClonedVoiceId ? Dialect.Unknown : Dialects.FromAccent(Caster.AccentOf(voice));
            cast.Add(new CastMember(p, voice, dialect, label));
        }
        return cast;
    }

    private string SystemFor(IReadOnlyList<CastMember> cast, Mission? m, TalkMode mode, string? reason)
    {
        var lead = cast[0];
        return Prompts.Speaker(lead.Person, m, Career, mode, reason, Settings.TalkLanguage, lead.Dialect,
            cast.Skip(1).Select(c => (c.Label, c.Person, c.Dialect)).ToList(), lead.Label);
    }

    private void PreloadClips(IReadOnlyList<CastMember> cast)
    {
        var mode = _talkMode;
        var language = Settings.TalkLanguage;
        var ct = _stop.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var c in cast)
                {
                    string lang = SpeechText.VoiceLanguage(c.Person.Language, c.Person.SpeaksNorwegian, null, language);
                    if (mode != TalkMode.Phone)
                    {
                        foreach (var line in lang == "no" ? ReactionLines.PanicNo : ReactionLines.PanicEn)
                            await Clips.EnsurePanicAsync(c.VoiceId, lang, lang == "no" ? Dialects.Adapt(line, c.Dialect) : line, ct);
                        foreach (var (_, line) in ReactionLines.All(lang))
                            await Clips.EnsureAsync(c.VoiceId, lang, lang == "no" ? Dialects.Adapt(line, c.Dialect) : line, ct);
                    }
                }
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
        var visible = Latest is { OnGround: false } t ? SightWatcher.Visible(t.Lat, t.Lon, t.HeadingTrue, AllSights()) : null;
        string? where = m is { Phase: MissionPhase.Negotiating or MissionPhase.Booked, Pickup: { } pickup } && _talkMode == TalkMode.Phone ? TripTiming.PilotLine(MinutesAwayFor(m), pickup.Name)
            : m is not null && _talkMode is TalkMode.Intercom or TalkMode.Debrief ? TripNow(m) : null;
        return Prompts.Situation(now, m, Latest, _monitor.Comfort, phone, visible, where);
    }

    /// <summary>What the people on board know about the trip, from the plan and where the aircraft really is.</summary>
    private string TripNow(Mission m)
    {
        var nav = Navigation.Next(m, Latest, CurrentAircraft());
        string? nearName = null;
        double nearKm = 0;
        if (Latest is { } t)
        {
            var near = WorldData.Places.Select(p => (p.Name, Km: Geo.DistanceM(t.Lat, t.Lon, p.Lat, p.Lon) / 1000))
                .Concat(AllSights().Select(s => (s.Name, Km: Geo.DistanceM(t.Lat, t.Lon, s.Lat, s.Lon) / 1000)))
                .OrderBy(x => x.Km).FirstOrDefault();
            if (near.Name is not null && near.Km < 60) (nearName, nearKm) = near;
        }
        string? landing = Latest is { } a && m.Dropoff is { } to
            ? Prompts.LandingNote(to.Name, Geo.DistanceNm(a.Lat, a.Lon, to.Lat, to.Lon), a.AglFt, a.VerticalFpm, a.GroundSpeedKt, a.OnGround) : null;
        return Prompts.WhereNow(m.Pickup?.Name, m.Dropoff?.Name, nav?.Name, nav?.DistanceNm ?? 0, nav?.Minutes ?? 0, nearName, nearKm, landing);
    }

    private double MinutesAwayFor(Mission m) => TripTiming.MinutesAway(Latest?.Lat ?? Career.LastLat, Latest?.Lon ?? Career.LastLon, m.Pickup, CurrentAircraft().CruiseKtas);

    private IEnumerable<PointOfInterest> AllSights() => WorldData.Sights.Concat(_map.Known);

    /// <summary>A few times a minute in the air: is there something new out of the window, and are we coming in to land.</summary>
    private void WatchTheView(Telemetry t, DateTimeOffset now)
    {
        if (_talk is null || _talkMode != TalkMode.Intercom || t.OnGround || (now - _lastSightCheck).TotalSeconds < 5) return;
        _lastSightCheck = now;
        _map.RefreshIfMoved(t.Lat, t.Lon, _stop.Token);
        if (_talk.IsSpeaking || (now - _lastPilotWords).TotalSeconds < 8 || OnBoardWithHarasser) return;

        if (_talkMission?.Dropoff is { } to && _approachCalledFor != _talkMission.Id && Geo.DistanceM(t.Lat, t.Lon, to.Lat, to.Lon) < 9000)
        {
            _approachCalledFor = _talkMission.Id;
            _lastSightComment = now;
            string side = SightWatcher.SideOf(t.HeadingTrue, Geo.BearingDeg(t.Lat, t.Lon, to.Lat, to.Lon));
            _talk.React($"You are coming in to {to.Name}, you can see it {side}. {to.Brief} The strip is {to.RunwayM:0} metres long. Comment on what you see. If it is a small strip someone nervous can ask if the pilot is really sure about landing there");
            return;
        }
        if ((now - _lastSightComment).TotalSeconds < 75) return;
        if (_sights.NextToMention(t.Lat, t.Lon, t.HeadingTrue, AllSights()) is { } next)
        {
            _lastSightComment = now;
            _talk.React($"Out of the window: {next.Describe()}. Someone points it out or says something about it if it is worth saying, as a person would, for example what they know about the place");
        }
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
        // Someone still talking grows their own bubble, so five sentences are one message, not five.
        if (_chat.Count > 0 && _chat[^1].Who == who && _chat[^1].FromPilot == pilot)
            _chat[^1] = _chat[^1] with { Text = _chat[^1].Text + " " + text };
        else
            _chat.Add(new ChatLine(DateTimeOffset.Now, who, text, pilot));
        if (!pilot) _talkMission?.Transcript.Add($"{who}: {text}");
        Changed?.Invoke();
    }

    /// <summary>Speech never waits for this. The newest exchange replaces a note taking still in progress.</summary>
    private void OnExchangeDone(Conversation talk)
    {
        if (_talk != talk) return;
        var m = _talkMission;
        _scribeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scribeCts = cts;
        string prompt = Prompts.ScribeUser(DateTimeOffset.Now, m?.Plan ?? new MissionPlan(), talk.TranscriptLines(Career.PilotName).TakeLast(30), m is null ? 15 : MinutesAwayFor(m));
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

    private int _offenceThisTalk;
    private bool _apologisedThisTalk;

    /// <summary>Nobody chats about the view with someone who just said that, and the flight's friendly moments stay quiet.</summary>
    private bool OnBoardWithHarasser => _talkMission?.PilotHarassed == true && Offences.StuckTogether(_talkMission);

    private void HandleOffence(Mission? m, PlanPatch p)
    {
        if (p.OffenceSeverity is not { } severity) return;
        bool stuck = Offences.StuckTogether(m);
        bool again = severity <= _offenceThisTalk;
        if (again && !(stuck && severity >= 2)) return;
        var pilotLines = _talk?.TranscriptLines(Career.PilotName).Where(l => l.StartsWith(Career.PilotName + ":")).ToList() ?? [];
        if (!Offences.SaidByPilot(p.OffenceQuote, pilotLines))
        {
            Log.Info("Game", $"Ignored an offence with no words from the pilot behind it: {p.OffenceNote}");
            return;
        }
        _offenceThisTalk = severity;
        var present = _talk?.Cast.Select(c => c.Person).ToList() ?? [];
        string what = string.IsNullOrWhiteSpace(p.OffenceNote) ? L.T("You behaved badly towards them") : p.OffenceNote!;
        string quote = p.OffenceQuote!.Trim();
        what += " " + L.F("The pilot said: \"{0}\"", quote);
        if (again)
        {
            // The note taker reads the whole talk every time, so the same words come back. Only new words are a new offence.
            if (m!.OffenceQuotes.Any(q => Offences.SameWords(q, quote))) return;
            Offences.Again(present, what, DateTimeOffset.Now);
        }
        else
        {
            bool wasLoaded = m?.Phase == MissionPhase.Loaded;
            Offences.Apply(Career, m, present, what, severity, DateTimeOffset.Now);
            if (wasLoaded && severity >= 2 && Sim.Connected && Sim.Stations.Count > 0) Sim.SetStations(StationMapper.Empty(Sim.Stations));
        }
        m?.OffenceQuotes.Add(quote);
        Log.Info("Game", $"Offence, severity {severity}: {what}");
        if (severity >= 2) Toast?.Invoke(what);
        if (stuck)
        {
            // The next thing they say comes from someone who knows exactly what happened, and they stay on board.
            if (_talk is { } talk) talk.ChangeScene(SystemFor(talk.Cast, m!, _talkMode, null));
            return;
        }
        if (severity >= 2)
        {
            _endCallSoon = true;
            if (m is not null) DespawnPeople(m);
        }
    }

    private void ApplyPatch(Mission? m, PlanPatch p)
    {
        HandleOffence(m, p);
        if (p.Apology == true && !_apologisedThisTalk && _offenceThisTalk == 0 && _talk is { } sorry)
        {
            _apologisedThisTalk = true;
            Offences.Apologise(sorry.Cast.Select(c => c.Person).ToList(), DateTimeOffset.Now);
            Log.Info("Game", "The pilot said sorry");
        }
        if (m is null)
        {
            if (p.CallIsOver == true && _talkMode == TalkMode.Phone) _endCallSoon = true;
            Save();
            Changed?.Invoke();
            return;
        }
        if (m.Phase == MissionPhase.Cancelled) { Save(); Changed?.Invoke(); return; }
        if (_talkMode is TalkMode.Intercom or TalkMode.Debrief) { Changed?.Invoke(); return; }
        if (p.TourPlace is { Length: > 1 } place && !m.Stops.Any(x => x.Name.Contains(place, StringComparison.OrdinalIgnoreCase)))
            _ = AddStopAsync(m, place);
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
        var spokenTo = _talk.Cast.Select(c => c.Person).ToList();
        if (transcript.Count >= 2)
        {
            foreach (var p in spokenTo)
            {
                p.Talks++;
                p.LastContact = DateTimeOffset.Now;
            }
            RememberTalk(spokenTo[0], m, transcript);
        }
        CloseTalkObject();
        Mic.Stop();
        Audio.Beep(520, 0.1);
        _talkMission = null;
        _endCallSoon = false;

        if (m is not null && mode == TalkMode.Phone && m.Phase == MissionPhase.Negotiating)
        {
            // One final pass over the whole call, so the booking reflects its last sentences even if the last note was cancelled.
            double away = MinutesAwayFor(m);
            _ = Task.Run(async () =>
            {
                try
                {
                    var patch = await Brain.ListenAsync(Prompts.ScribeUser(DateTimeOffset.Now, m.Plan, transcript, away), _stop.Token).ConfigureAwait(false);
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
                var now = DateTimeOffset.Now;
                m.Plan.PickupTime = TripTiming.Settle(m.Plan.PickupTime, TripTiming.Earliest(now, MinutesAwayFor(m)));
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

    private async Task AddStopAsync(Mission m, string place)
    {
        var found = await _map.FindAsync(place, _stop.Token).ConfigureAwait(false);
        Post(() =>
        {
            if (found is null) { Toast?.Invoke(L.F("Could not find {0} on the map", place)); return; }
            if (m.Stops.Any(x => x.Name == found.Name)) return;
            m.Stops.Add(new TourStop { Name = found.Name, Lat = found.Lat, Lon = found.Lon, Note = found.Note });
            m.Sightseeing = true;
            m.Plan.Changes.Add(new PlanChange(DateTimeOffset.Now, "sight", "", found.Name));
            Changed?.Invoke();
        });
    }

    public double SuggestedPrice(Mission m)
    {
        var tpl = MissionCatalog.ById(m.TemplateId);
        var (_, fuel, _) = PlanEvaluator.Evaluate(m, CurrentAircraft(), PilotKgNow());
        return Math.Round(Math.Max(tpl?.MinimumNok ?? 2500, fuel.TripHours * (tpl?.PricePerHourNok ?? 5200)) / 100) * 100;
    }

    private void ArchiveTalk()
    {
        if (_talk is not { } talk) return;
        var lines = talk.TranscriptLines(Career.PilotName).ToList();
        if (lines.Count == 0) return;
        Career.Talks.Add(new TalkRecord
        {
            When = DateTimeOffset.Now, Mode = _talkMode.ToString(), MissionTitle = _talkMission?.Title, Lines = lines, HungUp = talk.HungUpOnPilot, Offence = _offenceThisTalk,
            PersonIds = talk.Cast.Select(c => c.Person.Id).ToList(), Names = talk.Cast.Select(c => c.Person.Name).ToList(),
        });
        if (Career.Talks.Count > 500) Career.Talks.RemoveRange(0, Career.Talks.Count - 500);
    }

    private void CloseTalkObject()
    {
        ArchiveTalk();
        _scribeCts?.Cancel();
        _talk?.Dispose();
        _talk = null;
        Mic.HalfDuplexHold = false;
    }

    // ---- At the strip ---------------------------------------------------------------------------------------------

    public void CancelMission(string id) => Post(() =>
    {
        if (Career.MissionById(id) is not { } m || !Cancellation.Allowed(m)) return;
        if (_talkMission == m) HangUpNow();
        DespawnPeople(m);
        Cancellation.Apply(m, Career, DateTimeOffset.Now);
        Toast?.Invoke(L.F("Cancelled: {0}", m.Title));
        Save();
        Changed?.Invoke();
    });

    /// <summary>Moves a booked pickup: null means as soon as the aircraft can get there, otherwise by that many minutes.</summary>
    public void MoveTime(string id, int? byMinutes) => Post(() =>
    {
        if (Career.MissionById(id) is not { Phase: MissionPhase.Booked } m) return;
        var now = DateTimeOffset.Now;
        var earliest = TripTiming.Earliest(now, MinutesAwayFor(m));
        var wanted = byMinutes is { } by ? (m.Plan.PickupTime ?? now).AddMinutes(by) : earliest;
        var when = TripTiming.Move(m, Career, wanted, earliest, now);
        _ = RefreshForecast(m);
        Toast?.Invoke(L.F("Moved: {0}, {1} at {2}", m.Title, m.Pickup?.Name, Clock.ToLocal(when).ToString("HH:mm")));
        Save();
        Changed?.Invoke();
    });

    public void ForgetPerson(string id, bool delete) => Post(() =>
    {
        if (Career.PersonById(id) is not { } p) return;
        if (Forgetting.InUse(Career, p)) { Toast?.Invoke(L.F("{0} is in the middle of something with you right now", p.Name)); return; }
        var now = DateTimeOffset.Now;
        if (delete) Forgetting.Delete(Career, p);
        else Forgetting.Reset(p, now);
        Toast?.Invoke(L.F(delete ? "{0} is deleted" : "{0} no longer remembers you", p.Name));
        Save();
        Changed?.Invoke();
    });

    public void ForgetEveryone() => Post(() =>
    {
        int n = Forgetting.ResetEveryone(Career, DateTimeOffset.Now);
        Toast?.Invoke(L.F("{0} people no longer remember you", n));
        Save();
        Changed?.Invoke();
    });

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
        var a = CurrentAircraft();
        // Skip the scale and what goes on board is whatever they really brought, which is the whole point of the scale.
        // After weighing, it is the deal made at the scale, including anything or anyone left behind.
        bool weighed = m.Plan.BaggageWeighed;
        var load = LoadPlanner.Compute(new LoadRequest
        {
            Aircraft = a, PilotKg = PilotKgNow(),
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
        _sights.NewFlight();
        _approachCalledFor = null;

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
        if (tpl?.HasReturn == true && !m.PilotHarassed)
        {
            var when = m.Plan.ReturnTime ?? TripTiming.DefaultReturn(now, _rng);
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

    private void RememberTalk(Person who, Mission? m, IReadOnlyList<string> transcript)
    {
        if (!Brain.HasKey) return;
        string facts = Prompts.CallFacts(m, transcript.TakeLast(16));
        _ = Task.Run(async () =>
        {
            try
            {
                string memory = await Brain.OneLineAsync(Prompts.MemorySystem(who, Settings.TalkLanguage), facts, _stop.Token);
                Post(() =>
                {
                    who.Memories.Add(new MemoryNote(DateTimeOffset.Now, memory));
                    Save();
                    Changed?.Invoke();
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log.Error("Game", "Could not write the memory of a talk", ex); }
        });
    }

    private void WriteMemories(Mission m, FlightReview review, Person? lead)
    {
        if (lead is null || !Brain.HasKey) return;
        var tail = m.Transcript.TakeLast(14).ToList();
        string facts = Prompts.FlightFacts(m, review, tail);
        _ = Task.Run(async () =>
        {
            try
            {
                string memory = await Brain.OneLineAsync(Prompts.MemorySystem(lead, Settings.TalkLanguage), facts, _stop.Token);
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

    /// <summary>One line on where to fly now, refreshed as you move. For the map panel.</summary>
    public string? SelectedMissionId { get; set; }
    public string NavLine { get; private set; } = "";
    private DateTimeOffset _lastNav;

    public IReadOnlyList<PointOfInterest> KnownPlaces => _map.Known;
    public ViewModels.PlanePos? PlanePosition { get; private set; }
    public string ReachLine { get; private set; } = "";
    private readonly List<(double Lat, double Lon)> _trail = [];
    private DateTimeOffset _lastTrail;

    private void UpdateNav(Telemetry t, DateTimeOffset now)
    {
        if ((now - _lastNav).TotalSeconds < 0.2) return;
        _lastNav = now;
        var a = CurrentAircraft();
        var focus = ActiveMission ?? Career.MissionById(SelectedMissionId) ?? Career.OpenMissions.OrderBy(m => m.Plan.PickupTime ?? DateTimeOffset.MaxValue).FirstOrDefault();
        var nav = Navigation.Next(focus, t, a);
        NavLine = nav is null ? "" : L.F("Fly to {0}: course {1:000}, {2:0} nm, {3:0} min", nav.Name, nav.CourseMagnetic, nav.DistanceNm, nav.Minutes);

        if (t.OnGround && t.GroundSpeedKt < 1) _trail.Clear();
        else if ((now - _lastTrail).TotalSeconds >= 4)
        {
            _lastTrail = now;
            _trail.Add((t.Lat, t.Lon));
            if (_trail.Count > 1200) _trail.RemoveAt(0);
        }
        // How far the fuel reaches and still lands with the reserve.
        double reach = Math.Max(0, (t.FuelGal - a.BurnGph * 0.5) / a.BurnGph * a.CruiseKtas);
        ReachLine = L.F("The fuel reaches about {0:0} nm with reserve", reach);
        PlanePosition = new ViewModels.PlanePos(t.Lat, t.Lon, t.HeadingTrue, reach, _trail.ToList(), nav?.Lat, nav?.Lon);
        if (!t.OnGround) _map.RefreshIfMoved(t.Lat, t.Lon, _stop.Token);
    }

    private bool _simFactsLogged;
    private DateTimeOffset _firstSample;

    private void OnTelemetry(Telemetry t)
    {
        Latest = t;
        if (!_simFactsLogged)
        {
            if (_firstSample == default) _firstSample = t.Time;
            else if ((t.Time - _firstSample).TotalSeconds > 4)
            {
                _simFactsLogged = true;
                Log.Info("Sim", $"Aircraft: {t.Title}, weight {t.TotalWeightLb:0} lb, CG {t.CgPercent:0.0} percent, fuel {t.FuelGal:0.0} gal, on foot {t.OnFoot}");
                Log.Info("Sim", "Stations: " + string.Join("; ", Sim.Stations.Select(s => $"{s.SimIndex} '{s.Name}' = {s.Role}, {Sim.StationPounds.ElementAtOrDefault(s.SimIndex - 1):0} lb")));
                Log.Info("Sim", "Tanks: " + string.Join("; ", Sim.TankCapacityGal.Select(k => $"{k.Key} {k.Value:0.0} gal")));
                Log.Info("Sim", "People to place: " + string.Join(", ", _peopleTitles.Take(6)) + $" ({_peopleTitles.Count} in all)");
            }
        }
        Career.LastLat = t.Lat;
        Career.LastLon = t.Lon;
        var reserve = AircraftProfile.ForTitle(t.Title).BurnGph * 0.5;
        var flight = _monitor.Process(t, reserve);
        var now = DateTimeOffset.Now;
        var flow = _flow.Step(t, flight, now, _monitor.Comfort);
        foreach (var e in flow) OnFlow(e);
        StepPanic(t);
        foreach (var e in flight) OnFlightEvent(e, now);
        WatchTheView(t, now);
        UpdateNav(t, now);
        if (flow.Count > 0 || flight.Count > 0) Changed?.Invoke();
        LiveChanged?.Invoke();
    }

    private void OnFlow(FlowEvent e)
    {
        switch (e)
        {
            case PeopleShouldAppear p:
                SpawnPeople(p.Mission, p.Lat, p.Lon);
                break;
            case PeopleShouldDisappear gone:
                DespawnPeople(gone.Mission);
                break;
            case ArrivedAtPickup a:
                Toast?.Invoke(a.LateMinutes > 15 ? L.F("You are {0:0} minutes late at {1}", a.LateMinutes, a.Mission.Pickup?.Name) : L.F("Arrived at {0}. Shut down and go meet them.", a.Mission.Pickup?.Name));
                break;
            case CanMeetInPerson m:
                if (Latest?.OnFoot == true) MeetInPerson();
                break;
            case StopReached stop:
                _lastSightComment = DateTimeOffset.Now;
                if (!OnBoardWithHarasser) _talk?.React($"You are now flying over {stop.Stop.Name}, which is what you wanted to see. {stop.Stop.Note}. Be excited about it, in your own way");
                Toast?.Invoke(L.F("{0}: check", stop.Stop.Name));
                break;
            case MissionLanded l:
                if (!l.Mission.PilotHarassed && l.Mission.Stops.Where(x => !x.Seen).Select(x => x.Name).ToList() is { Count: > 0 } missed)
                    _talk?.React($"The pilot has landed without ever flying over {string.Join(" or ", missed)}, which is what you paid for. You are unhappy about it, and you say so");
                if (_talk is not null && _talkMission == l.Mission && Career.PersonById(l.Mission.CallerId) is { } who)
                {
                    _talkMode = TalkMode.Debrief;
                    _talk.ChangeScene(SystemFor(_talk.Cast, l.Mission, TalkMode.Debrief, null));
                    if (l.Mission.PilotHarassed)
                        _talk.React("The wheels are on the ground. You are down, you are safe, and you do not have to hold back any more. Everything you have been holding in since the pilot said that comes out now");
                }
                break;
            case ReadyToDeplane:
                Toast?.Invoke(L.T("Parked. Settle up with them on the card."));
                break;
        }
    }

    private readonly PanicWatcher _panic = new();

    private void StepPanic(Telemetry t)
    {
        switch (_panic.Step(t))
        {
            case PanicChange.Began:
                BeginPanic();
                break;
            case PanicChange.Ended:
                if (_talk?.Panicking == true)
                {
                    _talk.StopPanic();
                    _talk.React("That terrifying moment is over and you are still alive. You are shaking. Tell the pilot what you think of that, in your own way");
                }
                break;
            case PanicChange.Crashed:
                OnCrash();
                break;
        }
    }

    private void BeginPanic()
    {
        if (_talk is null || _talkMode is not (TalkMode.Intercom or TalkMode.Debrief)) return;
        Log.Info("Flight", "Everyone on board is panicking");
        _talk.StartPanic((member, lang) => (lang == "no" ? ReactionLines.PanicNo : ReactionLines.PanicEn)
            .Select(line => lang == "no" ? Dialects.Adapt(line, member.Dialect) : line).ToList());
    }

    /// <summary>The scream goes on through the impact, then the shock.</summary>
    private void OnCrash()
    {
        var talk = _talk;
        if (talk is null || _talkMode is not (TalkMode.Intercom or TalkMode.Debrief)) return;
        Log.Info("Flight", "Crash");
        BeginPanic();
        _ = Task.Delay(4000, _stop.Token).ContinueWith(d =>
        {
            if (d.IsCanceled) return;
            Post(() =>
            {
                talk.StopPanic();
                if (_talk == talk) talk.React("The aircraft has just crashed. You are alive, hurt and in complete shock. Nothing about this is normal");
            });
        }, TaskScheduler.Default);
    }

    private void OnFlightEvent(FlightEvent e, DateTimeOffset now)
    {
        Log.Info("Flight", $"{e.Kind}: {e.Text}");
        if (_talk is null || _talkMode is not (TalkMode.Intercom or TalkMode.Debrief)) return;
        var who = _talk.Person;
        string lang = SpeechText.VoiceLanguage(who.Language, who.SpeaksNorwegian, Mic.LastLanguage, Settings.TalkLanguage);
        // Same line for whoever says it, so the choice of words is made once.
        bool talkingNow = (now - _lastPilotWords).TotalSeconds < 3;
        // After what the pilot said, nobody says "nice landing". Danger still gets a reaction.
        if (_talkMission?.PilotHarassed == true && e.Kind is FlightEventKind.Takeoff or FlightEventKind.Landing or FlightEventKind.Sightseeing) return;

        var lines = ReactionLines.For(e.Kind, lang);
        if (lines.Count > 0 && !talkingNow)
        {
            string line = lines[_rng.Next(lines.Count)];
            _talk.Say((member, memberLang) => memberLang == "no" ? Dialects.Adapt(line, member.Dialect) : line);
        }

        bool worthWords = e.Kind is FlightEventKind.Landing or FlightEventKind.Takeoff or FlightEventKind.StallWarning or FlightEventKind.ExtremeBank or FlightEventKind.SteepBank
            or FlightEventKind.EngineStoppedInAir or FlightEventKind.LowFuel or FlightEventKind.DiveHard or FlightEventKind.Turbulence or FlightEventKind.LowPass;
        if (worthWords && !talkingNow && (now - _lastReaction).TotalSeconds > 15)
        {
            _lastReaction = now;
            _talk.React(e.Text);
        }
    }

    /// <summary>How much this job needs, from where the aircraft stands now. Null when there is no job to fuel for.</summary>
    public FuelAdvice? FuelNeed(Mission? m)
    {
        if (m is null) return null;
        double cached = Career.FuelCache.GetValueOrDefault(m.Plan.DropoffId ?? "");
        return FuelAdvisor.Advise(CurrentAircraft(), Latest?.Lat, Latest?.Lon, m, cached);
    }

    /// <summary>The place you are parked at, if any: that is where fuel comes from.</summary>
    public Place? PlaceHere() => Latest is { OnGround: true } t ? WorldData.Nearest(t.Lat, t.Lon, 2.5) : null;

    private void ScheduleChatter(DateTimeOffset now)
    {
        double chat = _talk?.Person.Chatty ?? 0.5;
        _nextChatter = now.AddSeconds(90 + (1 - chat) * 300 + _rng.NextDouble() * 120);
    }

    private void SpawnPeople(Mission m, double atLat, double atLon)
    {
        if (_spawned.ContainsKey(m.Id)) return;
        var ids = new List<uint>();
        int month = Clock.ToLocal(DateTimeOffset.Now).Month;
        var simList = Sim.ObjectTitles;
        int i = 0;
        foreach (var p in m.Truth.Passengers)
        {
            string gender = Career.PersonById(p.PersonId)?.Gender ?? "male";
            string title = Figures.Pick(gender, month, _peopleTitles, simList, _rng);
            var (lat, lon) = Geo.Offset(atLat, atLon, 60 + i * 40, 3 + i * 1.5);
            if (Sim.Spawn(title, lat, lon, _rng.NextDouble() * 360) is { } id) ids.Add(id);
            i++;
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
