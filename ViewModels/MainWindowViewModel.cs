using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using FlightCompanion.Models;
using FlightCompanion.Services;

namespace FlightCompanion.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly ISimConnectService _simConnectService;
        private readonly ISignalRService _signalRService;
        private readonly IMapService _mapService;
        private readonly IPhoneService _phoneService;
        private readonly IFlightPlanService _flightPlanService;
        private readonly IPayloadService _payloadService;
        private readonly IAudioService _audioService;

        private bool _isConnected;
        private bool _isSignalRConnected;
        private bool _isRecording;
        private string _connectionStatus = "Disconnected";
        private string _multiplayerStatus = "Not Connected";
        private string _playerName = "Pilot";
        private string _sessionId = string.Empty;
        private PhoneCall? _currentCall;
        private FlightPlan? _activeFlightPlan;
        private AircraftData _currentAircraftData;
        private float _audioLevel;
        private string _callerText = string.Empty;
        private bool _isCallIncoming;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(ConnectButtonText));
            }
        }

        public string ConnectButtonText => IsConnected ? "Disconnect SimConnect" : "Connect to MSFS";

        public bool IsSignalRConnected
        {
            get => _isSignalRConnected;
            set
            {
                _isSignalRConnected = value;
                OnPropertyChanged(nameof(IsSignalRConnected));
                OnPropertyChanged(nameof(MultiplayerButtonText));
            }
        }

        public string MultiplayerButtonText => IsSignalRConnected ? "Disconnect Multiplayer" : "Connect Multiplayer";

        public string MultiplayerStatus
        {
            get => _multiplayerStatus;
            set
            {
                _multiplayerStatus = value;
                OnPropertyChanged(nameof(MultiplayerStatus));
            }
        }

        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                _isRecording = value;
                OnPropertyChanged(nameof(IsRecording));
                OnPropertyChanged(nameof(RecordButtonText));
            }
        }

        public string RecordButtonText => IsRecording ? "Stop Recording" : "Start Recording";

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                _connectionStatus = value;
                OnPropertyChanged(nameof(ConnectionStatus));
            }
        }

        public string PlayerName
        {
            get => _playerName;
            set
            {
                _playerName = value;
                OnPropertyChanged(nameof(PlayerName));
            }
        }

        public string SessionId
        {
            get => _sessionId;
            set
            {
                _sessionId = value;
                OnPropertyChanged(nameof(SessionId));
            }
        }

        public PhoneCall? CurrentCall
        {
            get => _currentCall;
            set
            {
                _currentCall = value;
                OnPropertyChanged(nameof(CurrentCall));
                OnPropertyChanged(nameof(HasActiveCall));
            }
        }

        public bool HasActiveCall => CurrentCall != null;

        public bool IsCallIncoming
        {
            get => _isCallIncoming;
            set
            {
                _isCallIncoming = value;
                OnPropertyChanged(nameof(IsCallIncoming));
            }
        }

        public FlightPlan? ActiveFlightPlan
        {
            get => _activeFlightPlan;
            set
            {
                _activeFlightPlan = value;
                OnPropertyChanged(nameof(ActiveFlightPlan));
                OnPropertyChanged(nameof(HasActiveFlightPlan));
            }
        }

        public bool HasActiveFlightPlan => ActiveFlightPlan != null;

        public AircraftData CurrentAircraftData
        {
            get => _currentAircraftData;
            set
            {
                _currentAircraftData = value;
                OnPropertyChanged(nameof(CurrentAircraftData));
                OnPropertyChanged(nameof(FormattedLatitude));
                OnPropertyChanged(nameof(FormattedLongitude));
                OnPropertyChanged(nameof(FormattedAltitude));
                OnPropertyChanged(nameof(FormattedSpeed));
                OnPropertyChanged(nameof(FormattedHeading));
            }
        }

        public string FormattedLatitude => $"{CurrentAircraftData.Latitude:F6}°";
        public string FormattedLongitude => $"{CurrentAircraftData.Longitude:F6}°";
        public string FormattedAltitude => $"{CurrentAircraftData.Altitude:F0} ft";
        public string FormattedSpeed => $"{CurrentAircraftData.GroundSpeed:F0} kts";
        public string FormattedHeading => $"{CurrentAircraftData.Heading:F0}°";

        public float AudioLevel
        {
            get => _audioLevel;
            set
            {
                _audioLevel = value;
                OnPropertyChanged(nameof(AudioLevel));
                OnPropertyChanged(nameof(AudioLevelPercent));
            }
        }

        public double AudioLevelPercent => AudioLevel * 100;

        public string CallerText
        {
            get => _callerText;
            set
            {
                _callerText = value;
                OnPropertyChanged(nameof(CallerText));
            }
        }

        // Live transcription of what the pilot is saying (updates word-by-word as you talk).
        private string _liveTranscript = string.Empty;
        public string LiveTranscript
        {
            get => _liveTranscript;
            set { _liveTranscript = value; OnPropertyChanged(nameof(LiveTranscript)); }
        }

        public ObservableCollection<FlightPlan> FlightPlans { get; } = new();
        public ObservableCollection<PhoneCall> CallHistory { get; } = new();
        public ObservableCollection<ConversationMessage> ConversationHistory { get; } = new();

        // Trip-count readouts (ObservableCollection.Count doesn't raise a bindable change on its own).
        public string TripCountLabel => $"{FlightPlans.Count} booked";
        public bool HasNoTrips => FlightPlans.Count == 0;

        // --- Proximity payload prompt ("you're on the ground at X, load pax/baggage?") ---
        private PayloadPrompt? _payloadPrompt;
        private string _payloadStatus = string.Empty;
        public PayloadPrompt? PayloadPrompt
        {
            get => _payloadPrompt;
            set
            {
                _payloadPrompt = value;
                OnPropertyChanged(nameof(PayloadPrompt));
                OnPropertyChanged(nameof(HasPayloadPrompt));
                OnPropertyChanged(nameof(PayloadPromptTitle));
                OnPropertyChanged(nameof(PayloadPromptDetail));
            }
        }
        public bool HasPayloadPrompt => _payloadPrompt != null;
        public string PayloadPromptTitle => _payloadPrompt == null ? string.Empty
            : $"📍 At {_payloadPrompt.LocationName}";
        public string PayloadPromptDetail => _payloadPrompt == null ? string.Empty
            : $"{_payloadPrompt.PassengerCount} passengers · {_payloadPrompt.BaggageKg:F0} kg baggage · {_payloadPrompt.TotalKg:F0} kg total";
        public string PayloadStatus
        {
            get => _payloadStatus;
            set { _payloadStatus = value; OnPropertyChanged(nameof(PayloadStatus)); OnPropertyChanged(nameof(HasPayloadStatus)); }
        }
        public bool HasPayloadStatus => !string.IsNullOrEmpty(_payloadStatus);

        // Commands
        public ICommand ConnectCommand { get; }
        public ICommand ConnectMultiplayerCommand { get; }
        public ICommand CreateSessionCommand { get; }
        public ICommand JoinSessionCommand { get; }
        public ICommand AnswerCallCommand { get; }
        public ICommand RejectCallCommand { get; }
        public ICommand EndCallCommand { get; }
        public ICommand TestCallCommand { get; }
        public ICommand LoadPayloadCommand { get; }
        public ICommand DismissPayloadCommand { get; }

        public MainWindowViewModel(
            ISimConnectService simConnectService,
            ISignalRService signalRService,
            IMapService mapService,
            IPhoneService phoneService,
            IFlightPlanService flightPlanService,
            IPayloadService payloadService,
            IAudioService audioService)
        {
            _simConnectService = simConnectService;
            _signalRService = signalRService;
            _mapService = mapService;
            _phoneService = phoneService;
            _flightPlanService = flightPlanService;
            _payloadService = payloadService;
            _audioService = audioService;

            // Initialize commands
            ConnectCommand = new RelayCommand(async () => await ToggleSimConnectAsync());
            ConnectMultiplayerCommand = new RelayCommand(async () => await ToggleMultiplayerAsync());
            CreateSessionCommand = new RelayCommand(async () => await CreateSessionAsync());
            JoinSessionCommand = new RelayCommand(async () => await JoinSessionAsync());
            AnswerCallCommand = new RelayCommand<PhoneCall>(async call => await AnswerCallAsync(call));
            RejectCallCommand = new RelayCommand<PhoneCall>(async call => await RejectCallAsync(call));
            EndCallCommand = new RelayCommand(async () => await EndCallAsync());
            // Testing: trigger an incoming call on demand instead of waiting for the timer.
            TestCallCommand = new RelayCommand(async () => await _phoneService.InitiateRandomCallAsync());
            LoadPayloadCommand = new RelayCommand(() => { LoadCurrentPayload(); return Task.CompletedTask; });
            DismissPayloadCommand = new RelayCommand(() => { PayloadPrompt = null; return Task.CompletedTask; });

            // Subscribe to events
            _simConnectService.ConnectionStatusChanged += OnSimConnectConnectionChanged;
            _simConnectService.AircraftDataReceived += OnAircraftDataReceived;
            
            _signalRService.ConnectionChanged += OnSignalRConnectionChanged;
            _signalRService.PlayerDataReceived += OnPlayerDataReceived;
            
            _phoneService.IncomingCall += OnIncomingCall;
            _phoneService.CallAnswered += OnCallAnswered;
            _phoneService.CallEnded += OnCallEnded;
            _phoneService.CallerSpeaking += OnCallerSpeaking;
            _phoneService.PilotSpeaking += OnPilotSpeaking;
            _phoneService.PilotSpeakingLive += OnPilotSpeakingLive;
            _phoneService.MicLevel += OnMicLevel;
            _phoneService.FlightPlanCreated += OnFlightPlanCreated;
            
            _flightPlanService.FlightPlanCreated += OnFlightPlanAdded;
            _flightPlanService.FlightPlanUpdated += OnFlightPlanUpdated;

            _payloadService.LoadAvailable += OnPayloadLoadAvailable;
            _payloadService.PayloadApplied += OnPayloadApplied;
            _payloadService.LoadCleared += OnPayloadCleared;

            _audioService.VolumeChanged += OnVolumeChanged;

            FlightPlans.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(TripCountLabel));
                OnPropertyChanged(nameof(HasNoTrips));
            };
        }

        // --- Proximity payload handlers ---
        private void OnPayloadLoadAvailable(object? sender, PayloadPrompt prompt)
            => Dispatcher.UIThread.Post(() => { PayloadPrompt = prompt; PayloadStatus = string.Empty; });

        private void OnPayloadApplied(object? sender, PayloadPrompt prompt)
            => Dispatcher.UIThread.Post(() =>
            {
                PayloadPrompt = null;
                PayloadStatus = $"✅ Loaded {prompt.PassengerCount} pax + {prompt.BaggageKg:F0} kg at {prompt.LocationName}";
            });

        private void OnPayloadCleared(object? sender, EventArgs e)
            => Dispatcher.UIThread.Post(() => PayloadPrompt = null);

        private void LoadCurrentPayload()
        {
            var prompt = PayloadPrompt;
            if (prompt == null) return;
            if (!_payloadService.Load(prompt))
                PayloadStatus = "⚠️ Couldn't load — connect to MSFS and be in a flight first.";
        }

        private async Task ToggleSimConnectAsync()
        {
            try
            {
                if (IsConnected)
                {
                    await _simConnectService.DisconnectAsync();
                    ConnectionStatus = "Disconnected from MSFS";
                }
                else
                {
                    ConnectionStatus = "Connecting to MSFS...";
                    await _simConnectService.ConnectAsync();
                    await _mapService.InitializeAsync();
                    
                    // Wait longer for connection and don't override if already connected
                    await Task.Delay(5000);
                    if (!IsConnected)
                    {
                        ConnectionStatus = "Failed to connect to MSFS. Make sure MSFS 2024 is running and you're in a flight (not menu).";
                    }
                }
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"MSFS Connection Error: {ex.Message}";
                IsConnected = false;
            }
        }

        private async Task ToggleMultiplayerAsync()
        {
            try
            {
                if (IsSignalRConnected)
                {
                    await _signalRService.DisconnectAsync();
                    MultiplayerStatus = "Disconnected from multiplayer";
                }
                else
                {
                    MultiplayerStatus = "Connecting to multiplayer server...";
                    await _signalRService.ConnectAsync(PlayerName);
                    
                    // Wait a bit to see if connection succeeds
                    await Task.Delay(2000);
                    if (!IsSignalRConnected)
                    {
                        MultiplayerStatus = "Failed to connect to multiplayer server";
                    }
                }
            }
            catch (Exception ex)
            {
                MultiplayerStatus = $"Multiplayer Error: {ex.Message}";
                IsSignalRConnected = false;
            }
        }

        private async Task CreateSessionAsync()
        {
            if (!IsSignalRConnected)
            {
                MultiplayerStatus = "Connect to multiplayer first";
                return;
            }

            try
            {
                MultiplayerStatus = "Creating session...";
                await _signalRService.CreateSessionAsync();
            }
            catch (Exception ex)
            {
                MultiplayerStatus = $"Session creation failed: {ex.Message}";
            }
        }

        private async Task JoinSessionAsync()
        {
            if (string.IsNullOrEmpty(SessionId))
            {
                MultiplayerStatus = "Enter a session ID first";
                return;
            }

            if (!IsSignalRConnected)
            {
                MultiplayerStatus = "Connect to multiplayer first";
                return;
            }

            try
            {
                MultiplayerStatus = "Joining session...";
                await _signalRService.JoinSessionAsync(SessionId);
            }
            catch (Exception ex)
            {
                MultiplayerStatus = $"Failed to join session: {ex.Message}";
            }
        }

        private async Task AnswerCallAsync(PhoneCall? call)
        {
            if (call != null)
            {
                await _phoneService.AnswerCallAsync(call);
                IsCallIncoming = false;
            }
        }

        private async Task RejectCallAsync(PhoneCall? call)
        {
            if (call != null)
            {
                await _phoneService.RejectCallAsync(call);
                IsCallIncoming = false;
            }
        }

        private async Task EndCallAsync()
        {
            await _phoneService.EndCallAsync();
        }


        // Event handlers
        private void OnSimConnectConnectionChanged(object? sender, bool connected)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsConnected = connected;
                ConnectionStatus = connected ? "SimConnect Connected" : "SimConnect Disconnected";
            });
        }

        private void OnAircraftDataReceived(object? sender, AircraftData data)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                CurrentAircraftData = data;
                
                // Update map
                var playerData = new PlayerData
                {
                    PlayerId = PlayerName,
                    PlayerName = PlayerName,
                    Aircraft = data,
                    IsLocal = true,
                    LastUpdate = DateTime.Now
                };
                
                await _mapService.UpdatePlayerPositionAsync(playerData);
                
                // Send to other players
                if (_signalRService.IsConnected)
                {
                    await _signalRService.SendPlayerDataAsync(playerData);
                }
            });
        }

        private void OnSignalRConnectionChanged(object? sender, bool connected)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                IsSignalRConnected = connected;
                MultiplayerStatus = connected ? "Connected to multiplayer server" : "Disconnected from multiplayer server";
                await _mapService.UpdateConnectionStatusAsync(connected);
            });
        }

        private void OnPlayerDataReceived(object? sender, PlayerData playerData)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (!playerData.IsLocal)
                {
                    await _mapService.UpdatePlayerPositionAsync(playerData);
                }
            });
        }

        private void OnIncomingCall(object? sender, PhoneCall call)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CurrentCall = call;
                IsCallIncoming = true;
            });
        }

        private void OnCallAnswered(object? sender, PhoneCall call)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CurrentCall = call;
                IsCallIncoming = false;
            });
        }

        private void OnCallEnded(object? sender, PhoneCall call)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CallHistory.Add(call);
                CurrentCall = null;
                IsCallIncoming = false;
                CallerText = string.Empty;
                ConversationHistory.Clear(); // Clear conversation log for next call
            });
        }

        private void OnCallerSpeaking(object? sender, string text)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CallerText = text;
                
                // Add to conversation history
                if (_currentCall != null)
                {
                    ConversationHistory.Add(new ConversationMessage
                    {
                        Speaker = _currentCall.CallerName,
                        Message = text,
                        IsFromCaller = true
                    });
                }
            });
        }

        private void OnPilotSpeaking(object? sender, string text)
        {
            Dispatcher.UIThread.Post(() =>
            {
                LiveTranscript = string.Empty; // final arrived — clear the live line
                // Add pilot speech to conversation history
                ConversationHistory.Add(new ConversationMessage
                {
                    Speaker = "You (Pilot)",
                    Message = text,
                    IsFromCaller = false
                });
            });
        }

        // Live partial transcription — updates as the pilot speaks, before the final.
        private void OnPilotSpeakingLive(object? sender, string text)
        {
            Dispatcher.UIThread.Post(() => LiveTranscript = text);
        }

        // Live mic level (0..1) — drives the on-screen "voice recognition active" bar.
        private void OnMicLevel(object? sender, float level)
        {
            Dispatcher.UIThread.Post(() => AudioLevel = level);
        }

        private void OnFlightPlanCreated(object? sender, FlightPlan plan)
        {
            Dispatcher.UIThread.Post(() =>
            {
                FlightPlans.Add(plan);
                ActiveFlightPlan = plan;
                // Draw the trip route line on the map (pickup -> destination).
                if (plan.OriginLat != 0 && plan.DestinationLat != 0)
                    _ = _mapService.SetRouteAsync(new[]
                    {
                        (plan.OriginLat, plan.OriginLon),
                        (plan.DestinationLat, plan.DestinationLon)
                    });
            });
        }

        private void OnFlightPlanAdded(object? sender, FlightPlan plan)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!FlightPlans.Contains(plan))
                {
                    FlightPlans.Add(plan);
                }
            });
        }

        private void OnFlightPlanUpdated(object? sender, FlightPlan plan)
        {
            // Update in observable collection would happen automatically due to binding
        }

        private void OnVolumeChanged(object? sender, float volume)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AudioLevel = volume;
            });
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Simple RelayCommand implementation
    public class RelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public async void Execute(object? parameter)
        {
            await _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Func<T?, Task> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke((T?)parameter) ?? true;
        }

        public async void Execute(object? parameter)
        {
            await _execute((T?)parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}