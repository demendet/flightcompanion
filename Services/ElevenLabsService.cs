using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace FlightCompanion.Services
{
    public interface IElevenLabsService
    {
        Task<byte[]> GenerateSpeechAsync(string text, string voiceId);
        Task<Stream> GenerateSpeechStreamAsync(string text, string voiceId);
    }

    public class ElevenLabsService : IElevenLabsService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly IConfiguration _configuration;
        private readonly IDebugLogService _logger;

        public ElevenLabsService(IConfiguration configuration, IDebugLogService logger)
        {
            _configuration = configuration;
            _logger = logger;
            _apiKey = configuration["ApiKeys:ElevenLabs"] ?? throw new InvalidOperationException("ElevenLabs API key not configured");
            
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.elevenlabs.io/v1/")
            };
            _httpClient.DefaultRequestHeaders.Add("xi-api-key", _apiKey);
            _logger.LogVoice("ElevenLabs service initialized successfully");
        }

        // Rough language pick so ElevenLabs pronounces Norwegian as Norwegian (not Danish)
        // and English as English. Norwegian text has æ/ø/å and distinctly-Norwegian words.
        private static string DetectLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "en";
            var t = text.ToLowerInvariant();
            if (t.Contains('æ') || t.Contains('ø') || t.Contains('å')) return "no";
            string[] no = { "jeg", "ikke", "takk", "hei", " og ", " det ", " som ", " fra ", " deg ", " kan ", " skal ", " vær", " en tur", " fly" };
            foreach (var w in no) if (t.Contains(w)) return "no";
            return "en";
        }

        public async Task<byte[]> GenerateSpeechAsync(string text, string voiceId)
        {
            // Default voice if not specified
            if (string.IsNullOrEmpty(voiceId))
            {
                voiceId = "21m00Tcm4TlvDq8ikWAM"; // Rachel voice
            }
            
            var langCode = DetectLanguage(text);
            _logger.LogVoice($"Generating speech ({langCode}) with voice {voiceId}: {text[..Math.Min(50, text.Length)]}...");

            var request = new
            {
                text = text,
                model_id = "eleven_turbo_v2_5",
                language_code = langCode, // force Norwegian/English pronunciation (fixes the Danish accent)
                voice_settings = new
                {
                    stability = 0.5,
                    similarity_boost = 0.75,
                    style = 0.0,
                    use_speaker_boost = true
                }
            };

            try
            {
                _logger.LogVoice($"Sending text-to-speech request to ElevenLabs API (voice: {voiceId})");
                var response = await _httpClient.PostAsJsonAsync($"text-to-speech/{voiceId}", request);
                
                if (response.IsSuccessStatusCode)
                {
                    var audioData = await response.Content.ReadAsByteArrayAsync();
                    _logger.LogVoice($"Speech generation successful - {audioData.Length} bytes received");
                    return audioData;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"ElevenLabs API error - Status: {response.StatusCode}, Error: {error}", null, "Voice");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception during speech generation with voice {voiceId}", ex, "Voice");
            }

            // Return empty audio data if failed
            return Array.Empty<byte>();
        }

        public async Task<Stream> GenerateSpeechStreamAsync(string text, string voiceId)
        {
            // Default voice if not specified
            if (string.IsNullOrEmpty(voiceId))
            {
                voiceId = "21m00Tcm4TlvDq8ikWAM"; // Rachel voice
            }
            
            _logger.LogVoice($"Generating speech stream with voice {voiceId}: {text[..Math.Min(50, text.Length)]}...");

            var request = new
            {
                text = text,
                model_id = "eleven_turbo_v2_5",
                language_code = DetectLanguage(text),
                voice_settings = new
                {
                    stability = 0.5,
                    similarity_boost = 0.75,
                    style = 0.0,
                    use_speaker_boost = true
                },
                optimize_streaming_latency = 3
            };

            try
            {
                _logger.LogVoice($"Sending streaming text-to-speech request to ElevenLabs API (voice: {voiceId})");
                var response = await _httpClient.PostAsJsonAsync($"text-to-speech/{voiceId}/stream", request);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogVoice($"Speech stream generation successful for voice {voiceId}");
                    return await response.Content.ReadAsStreamAsync();
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"ElevenLabs streaming API error - Status: {response.StatusCode}, Error: {error}", null, "Voice");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception during speech stream generation with voice {voiceId}", ex, "Voice");
            }

            // Return empty stream if failed
            return new MemoryStream();
        }
    }
}