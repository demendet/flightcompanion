using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FlightCompanion.Models;
using Microsoft.Extensions.Configuration;

namespace FlightCompanion.Services
{
    public interface IOpenAIService
    {
        Task<string> GenerateFlightRequestAsync(Location origin, Location destination, string personality);
        Task<string> ProcessUserResponseAsync(string userInput, string conversationContext);
        Task<FlightPlan?> ExtractFlightPlanAsync(string conversation);
    }

    public class OpenAIService : IOpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly IConfiguration _configuration;
        private readonly IDebugLogService _logger;
        private readonly Random _random = new();

        public OpenAIService(IConfiguration configuration, IDebugLogService logger)
        {
            _configuration = configuration;
            _logger = logger;
            _apiKey = configuration["ApiKeys:OpenAI"] ?? throw new InvalidOperationException("LLM API key not configured");
            // Using DeepSeek's OpenAI-compatible API (chat/completions is drop-in compatible).
            // The key lives in the existing ApiKeys:OpenAI config field.
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.deepseek.com/v1/")
            };
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            _logger.LogPhoneCall("LLM service initialized (DeepSeek, deepseek-chat)");
        }

        public async Task<string> GenerateFlightRequestAsync(Location origin, Location destination, string personality)
        {
            _logger.LogPhoneCall($"Generating flight request: {origin.Name} -> {destination.Name}, personality: {personality}");
            
            var scenarios = new[]
            {
                "urgent medical transport",
                "scenic tourist flight",
                "supply delivery",
                "photography mission",
                "wildlife research",
                "fishing trip transport",
                "family vacation",
                "business meeting",
                "Northern Lights viewing",
                "emergency evacuation"
            };

            var scenario = scenarios[_random.Next(scenarios.Length)];
            var passengerCount = _random.Next(1, 5);
            var baggageWeight = _random.Next(10, 100);

            var systemPrompt = $@"You are {personality}. You need air transport from {origin.Name} to {destination.Name} in Northern Norway.
This is for: {scenario}
Passengers: {passengerCount}
Estimated baggage: {baggageWeight}kg

Start the conversation naturally, as if calling a pilot. Be conversational and realistic. Include:
- A greeting
- Your name and reason for calling
- Where you want to go and why
- Any special requirements or timeline
- Ask about availability and pricing

LANGUAGE: If your character is a local Norwegian (fisherman, pilot, researcher, resident), speak NORWEGIAN (Bokmål). If you are a foreign tourist, speak ENGLISH. Pick the one that fits your character and stay in it.

Keep it under 150 words and sound natural, like a real phone call.";

            var request = new
            {
                model = "deepseek-chat",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = "Generate the initial phone call request" }
                },
                temperature = 0.9,
                max_tokens = 200
            };

            try
            {
                _logger.LogPhoneCall($"Sending OpenAI request for flight scenario: {scenario}");
                var response = await _httpClient.PostAsJsonAsync("chat/completions", request);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonDocument>();
                    var content = result?.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();
                    
                    _logger.LogPhoneCall($"OpenAI generated flight request: {content?[..Math.Min(50, content?.Length ?? 0)]}...");
                    return content ?? "Hello, I need a flight. Can you help me?";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"OpenAI API error - Status: {response.StatusCode}, Error: {errorContent}", null, "PhoneCall");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception occurred during OpenAI flight request generation", ex, "PhoneCall");
            }

            _logger.LogWarning("Using fallback response for flight request", "PhoneCall");
            return "Hello, I need a flight. Can you help me?";
        }

        public async Task<string> ProcessUserResponseAsync(string userInput, string conversationContext)
        {
            _logger.LogPhoneCall($"Processing user response: {userInput}");
            var systemPrompt = @"You are continuing a phone conversation about arranging a flight in Northern Norway.
Previous context: " + conversationContext + @"

Respond naturally to what the pilot just said. Keep the conversation flowing. You might:
- Answer their questions
- Negotiate on price or timing
- Ask clarifying questions
- Confirm details
- Express urgency or flexibility

LANGUAGE: Reply in the SAME language the pilot just used — if they spoke Norwegian, reply in Norwegian (Bokmål); if English, reply in English. Match them.

Keep responses under 100 words and conversational.";

            var request = new
            {
                model = "deepseek-chat",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userInput }
                },
                temperature = 0.8,
                max_tokens = 150
            };

            try
            {
                _logger.LogPhoneCall("Sending user response to OpenAI for processing");
                var response = await _httpClient.PostAsJsonAsync("chat/completions", request);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonDocument>();
                    var content = result?.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();
                    
                    _logger.LogPhoneCall($"OpenAI response generated: {content?[..Math.Min(50, content?.Length ?? 0)]}...");
                    return content ?? "Yes, that sounds good.";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"OpenAI API error during response processing - Status: {response.StatusCode}, Error: {errorContent}", null, "PhoneCall");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception occurred during OpenAI response processing", ex, "PhoneCall");
            }

            _logger.LogWarning("Using fallback response for user input processing", "PhoneCall");
            return "Yes, that sounds good.";
        }

        public async Task<FlightPlan?> ExtractFlightPlanAsync(string conversation)
        {
            _logger.LogPhoneCall($"Extracting flight plan from conversation (length: {conversation.Length} chars)");
            var systemPrompt = @"Extract flight plan details from this conversation. Return JSON with:
- origin: location name
- destination: location name
- originLat: latitude
- originLon: longitude
- destinationLat: latitude
- destinationLon: longitude
- passengerCount: number
- baggageWeight: kg
- departureTime: ISO datetime
- customerName: name
- customerType: personality type
- agreedPrice: amount in NOK
- specialRequests: any special requirements

Locations are:
- Mollisjok Lodge: 69.5283, 24.3901
- Maggie Beach: 69.4562, 25.4585
- Ravnastua Fjellstue: 69.5269, 24.9556

If details are missing, make reasonable assumptions.";

            var request = new
            {
                model = "deepseek-chat",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"Extract flight plan from: {conversation}" }
                },
                temperature = 0.3,
                max_tokens = 300,
                response_format = new { type = "json_object" }
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync("chat/completions", request);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonDocument>();
                    var content = result?.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();
                    
                    if (!string.IsNullOrEmpty(content))
                    {
                        var planData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content);
                        
                        return new FlightPlan
                        {
                            Origin = GetStringValue(planData, "origin"),
                            Destination = GetStringValue(planData, "destination"),
                            OriginLat = GetDoubleValue(planData, "originLat"),
                            OriginLon = GetDoubleValue(planData, "originLon"),
                            DestinationLat = GetDoubleValue(planData, "destinationLat"),
                            DestinationLon = GetDoubleValue(planData, "destinationLon"),
                            PassengerCount = GetIntValue(planData, "passengerCount"),
                            BaggageWeight = GetDoubleValue(planData, "baggageWeight"),
                            DepartureTime = GetDateTimeValue(planData, "departureTime"),
                            CustomerName = GetStringValue(planData, "customerName"),
                            CustomerType = GetStringValue(planData, "customerType"),
                            AgreedPrice = GetDoubleValue(planData, "agreedPrice"),
                            SpecialRequests = GetStringValue(planData, "specialRequests"),
                            Status = FlightPlanStatus.Accepted
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error extracting flight plan from conversation", ex, "PhoneCall");
            }

            return null;
        }

        private string GetStringValue(Dictionary<string, JsonElement> dict, string key)
        {
            return dict.TryGetValue(key, out var value) ? value.GetString() ?? string.Empty : string.Empty;
        }

        private double GetDoubleValue(Dictionary<string, JsonElement> dict, string key)
        {
            return dict.TryGetValue(key, out var value) ? value.GetDouble() : 0.0;
        }

        private int GetIntValue(Dictionary<string, JsonElement> dict, string key)
        {
            return dict.TryGetValue(key, out var value) ? value.GetInt32() : 0;
        }

        private DateTime GetDateTimeValue(Dictionary<string, JsonElement> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                var str = value.GetString();
                if (!string.IsNullOrEmpty(str) && DateTime.TryParse(str, out var dt))
                {
                    return dt;
                }
            }
            return DateTime.Now.AddHours(1);
        }
    }
}