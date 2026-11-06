using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlightCompanion.Models;
using Microsoft.Extensions.Configuration;

namespace FlightCompanion.Services
{
    public interface IPhoneService
    {
        event EventHandler<PhoneCall>? IncomingCall;
        event EventHandler<PhoneCall>? CallAnswered;
        event EventHandler<PhoneCall>? CallEnded;
        event EventHandler<string>? CallerSpeaking;
        event EventHandler<string>? PilotSpeaking;
        event EventHandler<string>? PilotSpeakingLive; // live partial transcription as the pilot talks
        event EventHandler<float>? MicLevel;           // live mic level 0..1 for the on-screen bar
        event EventHandler<FlightPlan>? FlightPlanCreated;
        
        PhoneCall? CurrentCall { get; }
        List<PhoneCall> CallHistory { get; }
        
        Task InitiateRandomCallAsync();
        Task AnswerCallAsync(PhoneCall call);
        Task RejectCallAsync(PhoneCall call);
        Task EndCallAsync();
        Task ProcessUserSpeechAsync(string transcribedText);
    }

    public class PhoneService : IPhoneService
    {
        private readonly IOpenAIService _openAIService;
        private readonly IElevenLabsService _elevenLabsService;
        private readonly IAudioService _audioService;
        private readonly ISpeechRecognitionService _speechRecognitionService;
        private readonly IDebugLogService _logger;
        private readonly IConfiguration _configuration;
        private readonly Random _random = new();
        private System.Threading.Timer? _callTimer;
        private PhoneCall? _currentCall;
        private readonly List<PhoneCall> _callHistory = new();
        private string _conversationContext = string.Empty;
        private readonly List<Location> _locations;
        private readonly List<VoiceProfile> _voiceProfiles;

        public event EventHandler<PhoneCall>? IncomingCall;
        public event EventHandler<PhoneCall>? CallAnswered;
        public event EventHandler<PhoneCall>? CallEnded;
        public event EventHandler<string>? CallerSpeaking;
        public event EventHandler<string>? PilotSpeaking;
        public event EventHandler<string>? PilotSpeakingLive;
        public event EventHandler<float>? MicLevel;
        public event EventHandler<FlightPlan>? FlightPlanCreated;

        public PhoneCall? CurrentCall => _currentCall;
        public List<PhoneCall> CallHistory => _callHistory;

        public PhoneService(
            IOpenAIService openAIService,
            IElevenLabsService elevenLabsService,
            IAudioService audioService,
            ISpeechRecognitionService speechRecognitionService,
            IDebugLogService logger,
            IConfiguration configuration)
        {
            _openAIService = openAIService;
            _elevenLabsService = elevenLabsService;
            _audioService = audioService;
            _speechRecognitionService = speechRecognitionService;
            _logger = logger;
            _configuration = configuration;

            // Set up speech recognition event handler for voice-based conversations
            _speechRecognitionService.SpeechRecognized += OnSpeechRecognized;
            // Live partial transcription (words appear on screen as the pilot talks).
            _speechRecognitionService.SpeechRecognizing += (s, partial) => PilotSpeakingLive?.Invoke(this, partial);
            _speechRecognitionService.MicLevel += (s, lvl) => MicLevel?.Invoke(this, lvl);
            // Barge-in: the instant the pilot starts talking, cut off the AI's speech so the
            // conversation is fully interruptible (talk over it, like a real call).
            _speechRecognitionService.SpeechStarted += (s, e) =>
            {
                if (_audioService.IsPlaying)
                {
                    _audioService.StopPlayback();
                    _logger.LogPhoneCall("Barge-in — pilot started talking, cut off AI audio");
                }
            };

            // Load locations
            _locations = new List<Location>
            {
                new Location
                {
                    Name = _configuration["Locations:MollisjokLodge:Name"] ?? "Mollisjok Lodge",
                    Latitude = double.Parse(_configuration["Locations:MollisjokLodge:Latitude"] ?? "69.5283", CultureInfo.InvariantCulture),
                    Longitude = double.Parse(_configuration["Locations:MollisjokLodge:Longitude"] ?? "24.3901", CultureInfo.InvariantCulture),
                    BufferKm = double.Parse(_configuration["Locations:MollisjokLodge:BufferKm"] ?? "1.0", CultureInfo.InvariantCulture)
                },
                new Location
                {
                    Name = _configuration["Locations:MaggieBeach:Name"] ?? "Maggie Beach",
                    Latitude = double.Parse(_configuration["Locations:MaggieBeach:Latitude"] ?? "69.4562", CultureInfo.InvariantCulture),
                    Longitude = double.Parse(_configuration["Locations:MaggieBeach:Longitude"] ?? "25.4585", CultureInfo.InvariantCulture),
                    BufferKm = double.Parse(_configuration["Locations:MaggieBeach:BufferKm"] ?? "1.0", CultureInfo.InvariantCulture)
                },
                new Location
                {
                    Name = _configuration["Locations:Ravnastua:Name"] ?? "Ravnastua Fjellstue",
                    Latitude = double.Parse(_configuration["Locations:Ravnastua:Latitude"] ?? "69.5269", CultureInfo.InvariantCulture),
                    Longitude = double.Parse(_configuration["Locations:Ravnastua:Longitude"] ?? "24.9556", CultureInfo.InvariantCulture),
                    BufferKm = double.Parse(_configuration["Locations:Ravnastua:BufferKm"] ?? "1.0", CultureInfo.InvariantCulture)
                }
            };

            // Load voice profiles
            _voiceProfiles = configuration.GetSection("VoiceProfiles").Get<List<VoiceProfile>>() ?? new List<VoiceProfile>();

            // Start random call timer (between 30 seconds and 2 minutes)
            ScheduleNextCall();
            
            _logger.LogPhoneCall("Phone service initialized successfully");
        }

        private void ScheduleNextCall()
        {
            var delay = TimeSpan.FromSeconds(_random.Next(30, 120));
            _logger.LogPhoneCall($"Scheduling next random call in {delay.TotalSeconds:F0} seconds");
            _callTimer?.Dispose();
            _callTimer = new System.Threading.Timer(async _ => await InitiateRandomCallAsync(), null, delay, Timeout.InfiniteTimeSpan);
        }
        
        private async void OnSpeechRecognized(object? sender, string recognizedText)
        {
            if (_currentCall != null && _currentCall.State == CallState.Active)
            {
                _logger.LogPhoneCall($"Speech recognized during call: {recognizedText}");
                await ProcessUserSpeechAsync(recognizedText);
            }
        }

        public async Task InitiateRandomCallAsync()
        {
            if (_currentCall != null && _currentCall.State == CallState.Active)
            {
                // Don't initiate a new call if one is active
                _logger.LogPhoneCall("Skipping random call - another call is currently active");
                ScheduleNextCall();
                return;
            }
            
            _logger.LogPhoneCall("Initiating random phone call...");

            // Select random origin and destination
            var origin = _locations[_random.Next(_locations.Count)];
            var destination = _locations.Where(l => l.Name != origin.Name).OrderBy(_ => _random.Next()).First();

            // Select random voice profile
            VoiceProfile? profile = null;
            if (_voiceProfiles.Count > 0)
            {
                var profileIndex = _random.Next(_voiceProfiles.Count);
                profile = _voiceProfiles[profileIndex];
            }

            var callerName = profile?.Name ?? $"Caller {_random.Next(100, 999)}";
            var voiceId = profile?.VoiceId ?? "21m00Tcm4TlvDq8ikWAM";
            var personality = profile?.Personality ?? "A person needing air transport";

            // Create phone call
            _currentCall = new PhoneCall
            {
                CallerName = callerName,
                CallerVoiceId = voiceId,
                CallerPersonality = personality,
                State = CallState.Incoming,
                StartTime = DateTime.Now
            };

            // Generate initial request
            var initialRequest = await _openAIService.GenerateFlightRequestAsync(origin, destination, personality);
            _currentCall.ConversationContext = $"Caller: {callerName}\nRequest: {initialRequest}\nOrigin: {origin.Name}\nDestination: {destination.Name}";
            _conversationContext = _currentCall.ConversationContext;

            // Trigger incoming call event
            _logger.LogPhoneCall($"Incoming call from {callerName}: {initialRequest[..Math.Min(50, initialRequest.Length)]}...");
            IncomingCall?.Invoke(this, _currentCall);

            // Schedule next call
            ScheduleNextCall();
        }

        public async Task AnswerCallAsync(PhoneCall call)
        {
            if (call.State != CallState.Incoming) 
            {
                _logger.LogWarning($"Cannot answer call - call state is {call.State}, not Incoming", "PhoneCall");
                return;
            }

            _logger.LogPhoneCall($"Answering call from {call.CallerName}");
            call.State = CallState.Active;
            CallAnswered?.Invoke(this, call);

            // Start voice recognition for the call
            _logger.LogPhoneCall("Starting voice recognition for phone call");
            await _speechRecognitionService.StartContinuousRecognitionAsync();

            // Play initial greeting
            var greeting = _conversationContext.Split('\n')
                .FirstOrDefault(l => l.StartsWith("Request:"))?.Substring(8) ?? "Hello, I need a flight.";

            await SpeakAsync(greeting, call.CallerVoiceId);
        }

        public Task RejectCallAsync(PhoneCall call)
        {
            if (call.State != CallState.Incoming) 
            {
                _logger.LogWarning($"Cannot reject call - call state is {call.State}, not Incoming", "PhoneCall");
                return Task.CompletedTask;
            }

            _logger.LogPhoneCall($"Rejecting call from {call.CallerName}");
            call.State = CallState.Missed;
            call.EndTime = DateTime.Now;
            _callHistory.Add(call);
            _currentCall = null;
            
            CallEnded?.Invoke(this, call);
            return Task.CompletedTask;
        }

        public async Task EndCallAsync()
        {
            if (_currentCall == null) 
            {
                _logger.LogWarning("Cannot end call - no active call", "PhoneCall");
                return;
            }

            _logger.LogPhoneCall($"Ending call with {_currentCall.CallerName}");
            
            // Stop voice recognition
            await _speechRecognitionService.StopContinuousRecognitionAsync();
            _logger.LogPhoneCall("Voice recognition stopped for ended call");

            _currentCall.State = CallState.Ended;
            _currentCall.EndTime = DateTime.Now;

            // Try to extract flight plan from conversation
            if (!string.IsNullOrEmpty(_conversationContext))
            {
                _logger.LogPhoneCall("Extracting flight plan from conversation");
                var flightPlan = await _openAIService.ExtractFlightPlanAsync(_conversationContext);
                if (flightPlan != null)
                {
                    flightPlan.CustomerName = _currentCall.CallerName;
                    flightPlan.CustomerType = _currentCall.CallerPersonality;
                    _logger.LogPhoneCall($"Flight plan extracted: {flightPlan.Origin} -> {flightPlan.Destination}, {flightPlan.PassengerCount} passengers");
                    FlightPlanCreated?.Invoke(this, flightPlan);
                }
                else
                {
                    _logger.LogPhoneCall("No flight plan could be extracted from conversation");
                }
            }

            _callHistory.Add(_currentCall);
            var endedCall = _currentCall;
            _currentCall = null;
            _conversationContext = string.Empty;

            CallEnded?.Invoke(this, endedCall);
        }

        public async Task ProcessUserSpeechAsync(string transcribedText)
        {
            if (_currentCall == null || _currentCall.State != CallState.Active) 
            {
                _logger.LogWarning($"Cannot process speech - no active call (transcribed: {transcribedText})", "PhoneCall");
                return;
            }
            
            _logger.LogPhoneCall($"Processing user speech: {transcribedText}");
            
            // Notify UI that pilot is speaking
            PilotSpeaking?.Invoke(this, transcribedText);

            // Add user response to conversation
            _conversationContext += $"\nPilot: {transcribedText}";

            // Generate AI response
            var aiResponse = await _openAIService.ProcessUserResponseAsync(transcribedText, _conversationContext);
            
            // Add AI response to conversation
            _conversationContext += $"\nCaller: {aiResponse}";
            _currentCall.ConversationContext = _conversationContext;

            // Speak the response
            await SpeakAsync(aiResponse, _currentCall.CallerVoiceId);

            // Check if conversation seems complete (simple heuristic)
            var lowerResponse = aiResponse.ToLower();
            if (lowerResponse.Contains("thank you") && 
                (lowerResponse.Contains("see you") || lowerResponse.Contains("goodbye") || lowerResponse.Contains("perfect")))
            {
                // Auto-end call after a delay
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    await EndCallAsync();
                });
            }
        }

        private async Task SpeakAsync(string text, string voiceId)
        {
            _logger.LogPhoneCall($"Caller speaking (voice {voiceId}): {text[..Math.Min(50, text.Length)]}...");
            CallerSpeaking?.Invoke(this, text);

            try
            {
                // Generate speech
                var audioData = await _elevenLabsService.GenerateSpeechAsync(text, voiceId);

                if (audioData.Length > 0)
                {
                    // Play the audio (interruptible — barge-in stops it if the pilot talks over it).
                    _logger.LogPhoneCall($"Playing generated speech audio ({audioData.Length} bytes)");
                    await _audioService.PlayAudioAsync(audioData);
                }
                else
                {
                    _logger.LogWarning("No audio data generated for speech", "PhoneCall");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to generate or play speech: {text[..Math.Min(30, text.Length)]}...", ex, "PhoneCall");
            }
        }
    }
}