using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia.Threading;
using FlightCompanion.Game;
using FlightCompanion.Services;

namespace FlightCompanion.ViewModels;

public sealed class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}

public sealed class RelayCommand<T>(Action<T> run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) { if (parameter is T t) run(t); }
}

public enum Page { Board, Map, People, Logbook, Money, Settings }

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly GameHost _host;
    private readonly DispatcherTimer _live;
    private Picture? _pic;
    private Page _page = Page.Board;
    private string? _selected;
    private double _micLevel;
    private string _liveLine = "";
    private string _toast = "";
    private DateTimeOffset _toastUntil;
    private bool _scrollLockWas;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(GameHost host, bool live = true)
    {
        _host = host;
        host.Changed += OnChanged;
        host.Toast += t => Dispatcher.UIThread.Post(() =>
        {
            Toast = t;
            _toastUntil = DateTimeOffset.Now.AddSeconds(8);
        });

        AnthropicKey = host.Keys.Anthropic ?? "";
        ElevenLabsKey = host.Keys.ElevenLabs ?? "";
        PilotName = host.Career.PilotName;
        CompanyName = host.Career.CompanyName;
        PilotKg = host.Career.PilotKg;
        SpeakerModel = host.Settings.SpeakerModel;
        VoiceModelIndex = Math.Max(0, Array.FindIndex(ElevenLabs.Models, x => x.Id == host.Settings.VoiceModel));
        RadioEffect = host.Settings.RadioEffect;
        SpeechModelIndex = Array.FindIndex(SpeechModels.All, m => m.Id == host.Settings.SpeechModel);
        if (SpeechModelIndex < 0) SpeechModelIndex = 0;
        LanguageIndex = host.Settings.TalkLanguage switch { "en" => 1, "mixed" => 2, _ => 0 };
        UsingSpeakers = host.Settings.UsingSpeakers;
        DoNotDisturb = host.Settings.DoNotDisturb;

        Refresh();
        _live = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => LiveTick());
        if (live) _live.Start();
        host.Post(() => OnChanged());
    }

    /// <summary>Builds the picture right now. Only for the layout preview, which has no game loop timing to wait for.</summary>
    public void RefreshNow() => Pic = PictureBuilder.Build(_host, _selected);

    private void OnChanged()
    {
        // Called on the game loop: the picture is built right here, only the finished one crosses to the UI.
        var fresh = PictureBuilder.Build(_host, _selected);
        Dispatcher.UIThread.Post(() => Pic = fresh);
    }

    private void Refresh() => _host.Post(OnChanged);

    private void LiveTick()
    {
        MicLevel = _host.MicLevel;
        NavLine = _host.NavLine;
        if (_host.Latest is { } live)
        {
            MapAlt = L.F("{0:0} ft, {1:0} ft over the ground", live.AltFt, live.AglFt);
            MapSpeed = L.F("{0:0} kt, {1:+0;-0;0} ft/min", live.GroundSpeedKt, live.VerticalFpm);
            MapHeading = L.F("Course {0:000}", ((live.HeadingTrue - live.MagVarDeg) % 360 + 360) % 360);
        }
        WhenSoon = Pic.Card?.PickupAt is { } due ? UntilPickup((due - DateTimeOffset.Now).TotalMinutes) : "";
        ReachLine = _host.ReachLine;
        if (!ReferenceEquals(_plane, _host.PlanePosition)) { _plane = _host.PlanePosition; Raise(nameof(Plane)); }
        if (_volumeDirty && !Dragging())
        {
            _volumeDirty = false;
            _host.Settings.Save();
        }
        var t = _host.Latest;
        LiveLine = t is null ? L.T("No flight") :
            t.OnGround ? L.F("On the ground, {0:0} kt, fuel {1:0} gal", t.GroundSpeedKt, t.FuelGal)
                       : L.F("{0:0} ft above ground, {1:0} kt, fuel {2:0} gal, passengers' comfort {3:0}", t.AglFt, t.IasKt, t.FuelGal, _host.Comfort);
        if (_toast.Length > 0 && DateTimeOffset.Now > _toastUntil) Toast = "";

        // Scroll Lock mutes the mic from inside the sim: you can talk on Discord or to ATC without the passengers answering.
        bool scroll = (GetKeyState(0x91) & 1) != 0;
        if (scroll != _scrollLockWas)
        {
            _scrollLockWas = scroll;
            _host.SetMuted(scroll);
        }
    }

    private static bool Dragging() => (GetKeyState(0x01) & 0x8000) != 0;   // left mouse button: saves once the slider is let go

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    public Picture? Pic
    {
        get => _pic;
        private set
        {
            _pic = value;
            Raise();
            Raise(nameof(PeopleShown));
            Raise(nameof(TalksShown));
            Raise(nameof(SelectedPerson));
            Raise(nameof(HasSelectedPerson));
        }
    }

    private string? _selectedPersonId;
    private int _peopleFilter;

    public PersonRow? SelectedPerson => _pic?.People.FirstOrDefault(p => p.Id == _selectedPersonId);
    public bool HasSelectedPerson => SelectedPerson is not null;

    public System.Collections.Generic.IReadOnlyList<PersonRow> PeopleShown => (_pic?.People ?? []).Where(p => _peopleFilter switch
    {
        1 => p.TrustRaw > 15,
        2 => p.TrustRaw < -15,
        3 => p.FlightCount == 0,
        _ => true,
    }).ToList();

    private void Filter(int f) { _peopleFilter = f; Raise(nameof(PeopleShown)); }
    public ICommand FilterAll => new RelayCommand(() => Filter(0));
    public ICommand FilterFriends => new RelayCommand(() => Filter(1));
    public ICommand FilterWary => new RelayCommand(() => Filter(2));
    public ICommand FilterNotFlown => new RelayCommand(() => Filter(3));
    public ICommand OpenPerson => new RelayCommand<string>(id => { _selectedPersonId = id; Raise(nameof(SelectedPerson)); Raise(nameof(HasSelectedPerson)); });
    public ICommand ClosePerson => new RelayCommand(() => { _selectedPersonId = null; AskForget = ""; Raise(nameof(SelectedPerson)); Raise(nameof(HasSelectedPerson)); });

    // Forgetting cannot be undone, so every way of doing it asks once.
    private string _askForget = "";
    private string AskForget { get => _askForget; set { _askForget = value; Raise(nameof(AskingForget)); Raise(nameof(NotAskingForget)); Raise(nameof(AskForgetText)); Raise(nameof(AskingForgetAll)); Raise(nameof(NotAskingForgetAll)); } }
    public bool AskingForget => _askForget is "reset" or "delete";
    public bool NotAskingForget => !AskingForget;
    public bool AskingForgetAll => _askForget == "all";
    public bool NotAskingForgetAll => !AskingForgetAll;
    public string AskForgetText => _askForget == "delete"
        ? L.T("Delete this person for good? Anything booked with them is called off.")
        : L.T("Make them forget you? Trust, memories and history with them are wiped.");
    public ICommand AskResetPerson => new RelayCommand(() => AskForget = "reset");
    public ICommand AskDeletePerson => new RelayCommand(() => AskForget = "delete");
    public ICommand AskForgetAll => new RelayCommand(() => AskForget = "all");
    public ICommand KeepPeople => new RelayCommand(() => AskForget = "");
    public ICommand ConfirmForget => new RelayCommand(() =>
    {
        if (_selectedPersonId is { } id)
        {
            bool delete = _askForget == "delete";
            _host.ForgetPerson(id, delete);
            if (delete) { _selectedPersonId = null; Raise(nameof(SelectedPerson)); Raise(nameof(HasSelectedPerson)); }
        }
        AskForget = "";
    });
    public ICommand ConfirmForgetAll => new RelayCommand(() => { _selectedPersonId = null; AskForget = ""; Raise(nameof(SelectedPerson)); Raise(nameof(HasSelectedPerson)); _host.ForgetEveryone(); });
    public ICommand CallPerson => new RelayCommand<string>(id => { _selectedPersonId = null; Raise(nameof(SelectedPerson)); Raise(nameof(HasSelectedPerson)); Go(Page.Board); _host.CallPersonById(id); });

    /// <summary>Layout preview only.</summary>
    public void PreviewPeople(string? openId) { Go(Page.People); _selectedPersonId = openId; RefreshNow(); }
    public double MicLevel { get => _micLevel; private set { if (Math.Abs(_micLevel - value) > 0.005) { _micLevel = value; Raise(); } } }
    public string LiveLine { get => _liveLine; private set { if (_liveLine != value) { _liveLine = value; Raise(); } } }
    public string Toast { get => _toast; private set { _toast = value; Raise(); Raise(nameof(HasToast)); } }
    public bool HasToast => _toast.Length > 0;

    public bool OnBoard => _page == Page.Board;
    public bool OnMap => _page == Page.Map;
    public bool OnPeople => _page == Page.People;
    public bool OnLogbook => _page == Page.Logbook;
    public bool OnMoney => _page == Page.Money;
    public bool OnSettings => _page == Page.Settings;

    private void Go(Page p)
    {
        _page = p;
        Raise(nameof(OnBoard)); Raise(nameof(OnMap)); Raise(nameof(OnPeople)); Raise(nameof(OnLogbook)); Raise(nameof(OnMoney)); Raise(nameof(OnSettings));
    }

    public ICommand GoBoard => new RelayCommand(() => Go(Page.Board));
    public ICommand GoMap => new RelayCommand(() => Go(Page.Map));
    public ICommand GoPeople => new RelayCommand(() => Go(Page.People));
    public ICommand GoLogbook => new RelayCommand(() => Go(Page.Logbook));
    public ICommand GoMoney => new RelayCommand(() => Go(Page.Money));
    public ICommand GoSettings => new RelayCommand(() => Go(Page.Settings));

    /// <summary>The voices of the people you talk to, 0 to 200 percent. Takes effect at once and is remembered.</summary>
    public double VoiceVolume
    {
        get => _host.Settings.VoiceVolume * 100;
        set
        {
            _host.Settings.VoiceVolume = value / 100;
            _host.Audio.Volume = (float)(value / 100);
            _volumeDirty = true;
            Raise();
        }
    }

    /// <summary>How fast they talk, as a percent of the voice's normal pace. Applies from their next sentence.</summary>
    public double VoiceSpeed
    {
        get => _host.Settings.VoiceSpeed * 100;
        set
        {
            _host.Settings.VoiceSpeed = value / 100;
            _volumeDirty = true;
            Raise();
        }
    }

    private bool _volumeDirty;

    public ICommand AddVoices => new RelayCommand(_host.AddNorwegianVoices);
    public ICommand Answer => new RelayCommand(_host.Answer);
    public ICommand Decline => new RelayCommand(_host.Decline);
    public ICommand HangUp => new RelayCommand(_host.HangUp);
    public ICommand RequestJob => new RelayCommand(_host.RequestJob);
    public ICommand TestMic => new RelayCommand(_host.ToggleMicTest);
    /// <summary>Layout preview only.</summary>
    public void PreviewSelect(string id) => _selected = id;
    public ICommand Meet => new RelayCommand(_host.MeetInPerson);
    public ICommand Weigh => new RelayCommand(_host.Weigh);
    public ICommand Board => new RelayCommand(_host.Board);
    public ICommand Deplane => new RelayCommand(_host.Deplane);
    public ICommand LeaveTen => new RelayCommand(() => _host.LeaveBehind(10));
    public ICommand RefuelOneWay => new RelayCommand(() => { if (Pic?.Fuel is { } f) _host.Refuel(f.OneWayTarget); });
    public ICommand RefuelRoundTrip => new RelayCommand(() => { if (Pic?.Fuel is { } f) _host.Refuel(f.RoundTripTarget); });
    public ICommand RefuelFull => new RelayCommand(() => { if (Pic?.Fuel is { } f) _host.Refuel(f.FullTarget); });
    public ICommand CallCustomer => new RelayCommand<string>(_host.CallPerson);
    public ICommand SelectJob => new RelayCommand<string>(id => { _selected = id; _host.SelectedMissionId = id; AskingToCancel = false; Refresh(); });
    private PlanePos? _plane;
    public PlanePos? Plane => _plane;
    private string _reachLine = "";
    public string ReachLine { get => _reachLine; private set { if (_reachLine != value) { _reachLine = value; Raise(); } } }
    /// <summary>Layout preview only.</summary>
    public void PreviewMap() { Go(Page.Map); RefreshNow(); }
    public void PreviewPlane(PlanePos p) { _plane = p; Raise(nameof(Plane)); }
    private string _mapAlt = "", _mapSpeed = "", _mapHeading = "";
    public string MapAlt { get => _mapAlt; private set { if (_mapAlt != value) { _mapAlt = value; Raise(); } } }
    public string MapSpeed { get => _mapSpeed; private set { if (_mapSpeed != value) { _mapSpeed = value; Raise(); } } }
    public string MapHeading { get => _mapHeading; private set { if (_mapHeading != value) { _mapHeading = value; Raise(); } } }

    // Logbook tabs
    private int _logTab;
    public bool OnLogFlights => _logTab == 0;
    public bool OnLogTalks => _logTab > 0;
    private void LogTab(int tab) { _logTab = tab; _talksShown = null; Raise(nameof(OnLogFlights)); Raise(nameof(OnLogTalks)); Raise(nameof(TalksShown)); }
    public ICommand LogFlights => new RelayCommand(() => LogTab(0));
    public ICommand LogTalks => new RelayCommand(() => LogTab(1));
    public ICommand LogOffences => new RelayCommand(() => LogTab(2));
    /// <summary>Kept as the same list until something new comes in, or every opened conversation would snap shut on each update.</summary>
    private System.Collections.Generic.IReadOnlyList<TalkRow>? _talksShown, _talksFrom;
    public System.Collections.Generic.IReadOnlyList<TalkRow> TalksShown
    {
        get
        {
            var all = _pic?.Talks ?? [];
            if (_talksShown is null || !ReferenceEquals(_talksFrom, all))
            {
                _talksFrom = all;
                _talksShown = _logTab == 2 ? all.Where(x => x.Offence).ToList() : all;
            }
            return _talksShown;
        }
    }
    /// <summary>Layout preview only.</summary>
    public void PreviewLog(int tab) { Go(Page.Logbook); LogTab(tab); RefreshNow(); }
    private string _whenSoon = "";
    public string WhenSoon { get => _whenSoon; private set { if (_whenSoon != value) { _whenSoon = value; Raise(); } } }
    private static string UntilPickup(double minutes) => minutes switch
    {
        < -1 => L.F("{0:0} min late", -minutes),
        < 1 => L.T("Now"),
        < 180 => L.F("In {0:0} min", minutes),
        _ => L.F("In {0:0} hours", minutes / 60),
    };
    public ICommand MoveTime => new RelayCommand<string>(by =>
    {
        if (Pic.Card?.Id is { } id) _host.MoveTime(id, by == "now" ? null : int.Parse(by!, System.Globalization.CultureInfo.InvariantCulture));
    });
    private string _navLine = "";
    public string NavLine { get => _navLine; private set { if (_navLine != value) { _navLine = value; Raise(); Raise(nameof(HasNav)); } } }
    public bool HasNav => _navLine.Length > 0;

    private bool _askingToCancel;
    /// <summary>Cancelling asks once first, because a stray click would otherwise leave people standing at a strip.</summary>
    public bool AskingToCancel
    {
        get => _askingToCancel;
        private set { _askingToCancel = value; Raise(); Raise(nameof(NotAskingToCancel)); }
    }
    public bool NotAskingToCancel => !_askingToCancel;
    public ICommand AskCancel => new RelayCommand(() => AskingToCancel = true);
    public ICommand KeepFlight => new RelayCommand(() => AskingToCancel = false);
    public ICommand ConfirmCancel => new RelayCommand<string>(id => { AskingToCancel = false; _host.CancelMission(id); });

    // Settings
    public string AnthropicKey { get; set; } = "";
    public string ElevenLabsKey { get; set; } = "";
    public string PilotName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public double PilotKg { get; set; }
    public string SpeakerModel { get; set; } = "";
    public bool UsingSpeakers { get; set; }
    public bool DoNotDisturb { get; set; }
    public string[] VoiceModelLabels { get; } = ElevenLabs.Models.Select(x => x.Label).ToArray();
    public int VoiceModelIndex { get; set; }
    public bool RadioEffect { get; set; }
    public string[] SpeechModelLabels { get; } = SpeechModels.All.Select(m => m.Label).ToArray();
    public int SpeechModelIndex { get; set; }
    public int LanguageIndex { get; set; }
    public string[] Languages { get; } = ["Norsk", "English", "Norsk og engelsk, som i virkeligheten"];
    public string[] SpeakerModels { get; } = ["claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5"];

    public ICommand SaveSettings => new RelayCommand(() =>
    {
        _host.Keys.Anthropic = AnthropicKey;
        _host.Keys.ElevenLabs = ElevenLabsKey;
        _host.Settings.SpeakerModel = SpeakerModel;
        _host.Settings.VoiceModel = ElevenLabs.Models[Math.Clamp(VoiceModelIndex, 0, ElevenLabs.Models.Length - 1)].Id;
        _host.Settings.RadioEffect = RadioEffect;
        _host.Settings.SpeechModel = SpeechModels.All[Math.Clamp(SpeechModelIndex, 0, SpeechModels.All.Length - 1)].Id;
        _host.Settings.TalkLanguage = LanguageIndex switch { 1 => "en", 2 => "mixed", _ => "no" };
        _host.LanguageChanged();
        _host.Settings.UsingSpeakers = UsingSpeakers;
        _host.Settings.DoNotDisturb = DoNotDisturb;
        _host.Settings.Save();
        string pilot = PilotName, company = CompanyName;
        double kg = PilotKg;
        _host.Post(() =>
        {
            _host.Career.PilotName = pilot;
            _host.Career.CompanyName = company;
            _host.Career.PilotKg = kg;
            _host.Save();
        });
        _host.KeysChanged();
        Toast = L.T("Settings saved");
        _toastUntil = DateTimeOffset.Now.AddSeconds(4);
        Refresh();
    });

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
