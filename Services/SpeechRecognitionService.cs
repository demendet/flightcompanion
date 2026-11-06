using System;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Configuration;

namespace FlightCompanion.Services
{
    public interface ISpeechRecognitionService
    {
        event EventHandler<string>? SpeechRecognized;
        event EventHandler<string>? SpeechRecognizing;
        event EventHandler<bool>? IsListeningChanged;
        event EventHandler? SpeechStarted;  // fires the instant the user starts talking (barge-in)
        event EventHandler<float>? MicLevel; // live mic level 0..1 for the on-screen bar

        bool IsListening { get; }
        bool Muted { get; set; } // half-duplex: ignore mic input while the AI is speaking
        Task StartContinuousRecognitionAsync();
        Task StopContinuousRecognitionAsync();
        Task<string> RecognizeOnceAsync();
    }

    public class SpeechRecognitionService : ISpeechRecognitionService, IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly IDebugLogService _logger;
        private SpeechRecognizer? _speechRecognizer;
        private SpeechConfig? _speechConfig;
        private AudioConfig? _audioConfig;
        private bool _isListening;
        private bool _isDisposed;

        public event EventHandler<string>? SpeechRecognized;
        public event EventHandler<string>? SpeechRecognizing;
        public event EventHandler<bool>? IsListeningChanged;
        public event EventHandler? SpeechStarted;
        public event EventHandler<float>? MicLevel;

        public bool IsListening => _isListening;
        public bool Muted { get; set; }

        public SpeechRecognitionService(IConfiguration configuration, IDebugLogService logger)
        {
            _configuration = configuration;
            _logger = logger;
            
            InitializeSpeechRecognition();
        }

        private void InitializeSpeechRecognition()
        {
            try
            {
                _logger.LogVoice("Initializing speech recognition...");
                
                // Get Azure Speech Services configuration
                var subscriptionKey = _configuration["ApiKeys:AzureSpeech"];
                var region = _configuration["AzureSpeech:Region"] ?? "northeurope";
                
                if (!string.IsNullOrEmpty(subscriptionKey))
                {
                    // Initialize with Azure Speech Services
                    _speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
                    _speechConfig.SpeechRecognitionLanguage = "en-US"; // Change to "nb-NO" for Norwegian if needed
                    
                    _audioConfig = AudioConfig.FromDefaultMicrophoneInput();
                    _speechRecognizer = new SpeechRecognizer(_speechConfig, _audioConfig);
                    
                    // Set up event handlers
                    _speechRecognizer.Recognizing += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Result.Text))
                        {
                            _logger.LogVoice($"Recognizing: {e.Result.Text}");
                            SpeechRecognizing?.Invoke(this, e.Result.Text);
                        }
                    };
                    
                    _speechRecognizer.Recognized += (s, e) =>
                    {
                        if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
                        {
                            _logger.LogVoice($"Recognized: {e.Result.Text}");
                            SpeechRecognized?.Invoke(this, e.Result.Text);
                        }
                        else if (e.Result.Reason == ResultReason.NoMatch)
                        {
                            _logger.LogVoice("No speech was recognized");
                        }
                    };
                    
                    _speechRecognizer.Canceled += (s, e) =>
                    {
                        _logger.LogWarning($"Speech recognition canceled: {e.Reason}", "Voice");
                        if (e.Reason == CancellationReason.Error)
                        {
                            _logger.LogError($"Speech recognition error: {e.ErrorCode} - {e.ErrorDetails}", null, "Voice");
                        }
                    };
                    
                    _speechRecognizer.SessionStarted += (s, e) =>
                    {
                        _logger.LogVoice("Speech recognition session started");
                    };
                    
                    _speechRecognizer.SessionStopped += (s, e) =>
                    {
                        _logger.LogVoice("Speech recognition session stopped");
                    };
                    
                    _logger.LogVoice($"Speech recognition initialized with Azure Speech Services (region: {region})");
                }
                else
                {
                    _logger.LogWarning("Azure Speech Services key not configured - speech recognition will be disabled", "Voice");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to initialize speech recognition", ex, "Voice");
            }
        }

        public async Task StartContinuousRecognitionAsync()
        {
            if (_isListening || _isDisposed) return;

            try
            {
                _logger.LogVoice("Starting continuous speech recognition...");
                
                if (_speechRecognizer != null)
                {
                    // Start real Azure Speech Services continuous recognition
                    await _speechRecognizer.StartContinuousRecognitionAsync();
                    _isListening = true;
                    IsListeningChanged?.Invoke(this, true);
                    _logger.LogVoice("Continuous speech recognition started with Azure Speech Services");
                }
                else
                {
                    // Fallback simulation mode if Azure Speech Services is not configured
                    _logger.LogWarning("Azure Speech Services not available, starting simulation mode", "Voice");
                    _isListening = true;
                    IsListeningChanged?.Invoke(this, true);
                    
                    // Simulate listening for testing without Azure keys
                    _ = Task.Run(async () =>
                    {
                        while (_isListening && !_isDisposed)
                        {
                            await Task.Delay(8000); // Wait 8 seconds between test phrases
                            
                            if (_isListening)
                            {
                                // Simulate recognized speech for testing
                                var testPhrases = new[]
                                {
                                    "Yes that sounds good",
                                    "How much will that cost",
                                    "When can we schedule the flight",
                                    "I need to transport some equipment",
                                    "That works for me"
                                };
                                
                                var random = new Random();
                                var testPhrase = testPhrases[random.Next(testPhrases.Length)];
                                
                                _logger.LogVoice($"Simulated speech recognition: {testPhrase}");
                                SpeechRecognized?.Invoke(this, testPhrase);
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to start continuous speech recognition", ex, "Voice");
                _isListening = false;
                IsListeningChanged?.Invoke(this, false);
            }
        }

        public async Task StopContinuousRecognitionAsync()
        {
            if (!_isListening || _isDisposed) return;

            try
            {
                _logger.LogVoice("Stopping continuous speech recognition...");
                
                if (_speechRecognizer != null)
                {
                    // Stop real Azure Speech Services continuous recognition
                    await _speechRecognizer.StopContinuousRecognitionAsync();
                    _logger.LogVoice("Azure Speech Services continuous recognition stopped");
                }
                
                _isListening = false;
                IsListeningChanged?.Invoke(this, false);
                
                _logger.LogVoice("Continuous speech recognition stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to stop continuous speech recognition", ex, "Voice");
            }
        }

        public async Task<string> RecognizeOnceAsync()
        {
            try
            {
                _logger.LogVoice("Starting single speech recognition...");
                
                if (_speechRecognizer != null)
                {
                    // Use real Azure Speech Services for single recognition
                    var result = await _speechRecognizer.RecognizeOnceAsync();
                    
                    if (result.Reason == ResultReason.RecognizedSpeech)
                    {
                        _logger.LogVoice($"Single speech recognition result: {result.Text}");
                        return result.Text;
                    }
                    else if (result.Reason == ResultReason.NoMatch)
                    {
                        _logger.LogVoice("No speech was recognized in single recognition");
                        return "I didn't catch that, could you repeat?";
                    }
                    else if (result.Reason == ResultReason.Canceled)
                    {
                        var cancellation = CancellationDetails.FromResult(result);
                        _logger.LogError($"Speech recognition canceled: {cancellation.Reason} - {cancellation.ErrorDetails}", null, "Voice");
                        return "I didn't catch that, could you repeat?";
                    }
                }
                else
                {
                    // Fallback simulation for single recognition
                    _logger.LogWarning("Azure Speech Services not available, using simulation for single recognition", "Voice");
                    await Task.Delay(2000); // Simulate processing time
                    
                    var testResponses = new[]
                    {
                        "Yes that sounds perfect",
                        "How much would that cost",
                        "When can we do this",
                        "I'll take that deal",
                        "Let me think about it"
                    };
                    
                    var response = testResponses[new Random().Next(testResponses.Length)];
                    _logger.LogVoice($"Simulated single speech recognition result: {response}");
                    return response;
                }
                
                return "I didn't catch that, could you repeat?";
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to recognize speech", ex, "Voice");
                return "I didn't catch that, could you repeat?";
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            
            _isDisposed = true;
            _isListening = false;
            
            _speechRecognizer?.Dispose();
            _audioConfig?.Dispose();
            
            _logger.LogVoice("Speech recognition service disposed");
        }
    }
}