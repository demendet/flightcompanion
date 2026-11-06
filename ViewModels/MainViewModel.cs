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

public enum Page { Board, People, Logbook, Money, Settings }

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

    public Picture? Pic { get => _pic; private set { _pic = value; Raise(); } }
    public double MicLevel { get => _micLevel; private set { if (Math.Abs(_micLevel - value) > 0.005) { _micLevel = value; Raise(); } } }
    public string LiveLine { get => _liveLine; private set { if (_liveLine != value) { _liveLine = value; Raise(); } } }
    public string Toast { get => _toast; private set { _toast = value; Raise(); Raise(nameof(HasToast)); } }
    public bool HasToast => _toast.Length > 0;

    public bool OnBoard => _page == Page.Board;
    public bool OnPeople => _page == Page.People;
    public bool OnLogbook => _page == Page.Logbook;
    public bool OnMoney => _page == Page.Money;
    public bool OnSettings => _page == Page.Settings;

    private void Go(Page p)
    {
        _page = p;
        Raise(nameof(OnBoard)); Raise(nameof(OnPeople)); Raise(nameof(OnLogbook)); Raise(nameof(OnMoney)); Raise(nameof(OnSettings));
    }

    public ICommand GoBoard => new RelayCommand(() => Go(Page.Board));
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
    public ICommand RefuelFull => new RelayCommand(() => _host.Refuel(53));
    public ICommand RefuelHalf => new RelayCommand(() => _host.Refuel(26.5));
    public ICommand CallCustomer => new RelayCommand<string>(_host.CallPerson);
    public ICommand SelectJob => new RelayCommand<string>(id => { _selected = id; Refresh(); });

    // Settings
    public string AnthropicKey { get; set; } = "";
    public string ElevenLabsKey { get; set; } = "";
    public string PilotName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public double PilotKg { get; set; }
    public string SpeakerModel { get; set; } = "";
    public bool UsingSpeakers { get; set; }
    public bool DoNotDisturb { get; set; }
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
