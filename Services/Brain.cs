using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using FlightCompanion.Game;
using FlightCompanion.Game.Missions;

namespace FlightCompanion.Services;

public sealed record Turn(bool FromPilot, string Text);

/// <summary>Claude: one model speaks as the person, one listens and fills in the card. Speech never waits for the notes.</summary>
public sealed class Brain(KeyStore keys, AppSettings settings)
{
    private AnthropicClient? _client;
    private string? _clientKey;

    public bool HasKey => !string.IsNullOrEmpty(keys.Anthropic);

    private AnthropicClient Client()
    {
        string key = keys.Anthropic ?? throw new InvalidOperationException("No Anthropic API key set");
        if (_client is null || _clientKey != key)
        {
            _client = new AnthropicClient { ApiKey = key };
            _clientKey = key;
        }
        return _client;
    }

    /// <summary>Haiku can run without thinking, which is what a live call wants. Opus always thinks, so there it runs on low effort.</summary>
    private static ThinkingConfigParam? FastThinking(string model)
    {
        if (!model.Contains("haiku", StringComparison.OrdinalIgnoreCase)) return null;
        return new ThinkingConfigDisabled();
    }

    public async IAsyncEnumerable<string> SpeakAsync(string system, IReadOnlyList<Turn> history, [EnumeratorCancellation] CancellationToken ct)
    {
        var parameters = new MessageCreateParams
        {
            Model = settings.SpeakerModel,
            MaxTokens = 300,
            Thinking = FastThinking(settings.SpeakerModel),
            OutputConfig = new OutputConfig { Effort = Effort.Low },
            System = new List<TextBlockParam> { new() { Text = system, CacheControl = new CacheControlEphemeral() } },
            Messages = ToMessages(history),
        };
        await foreach (var ev in Client().Messages.CreateStreaming(parameters, ct))
        {
            if (ev.TryPickContentBlockDelta(out var d) && d.Delta.TryPickText(out var t) && t.Text.Length > 0)
                yield return t.Text;
        }
    }

    public async Task<string> OneLineAsync(string system, string user, CancellationToken ct)
    {
        var msg = await Client().Messages.Create(new MessageCreateParams
        {
            Model = settings.ScribeModel,
            MaxTokens = 200,
            Thinking = FastThinking(settings.ScribeModel),
            OutputConfig = new OutputConfig { Effort = Effort.Low },
            System = system,
            Messages = [new UserMessageParam { Content = user }],
        }, ct);
        return string.Concat(msg.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
    }

    private static readonly IReadOnlyDictionary<string, JsonElement> PatchSchema = BuildSchema();

    private static Dictionary<string, JsonElement> BuildSchema()
    {
        static object Nullable(string type) => new { type = new[] { type, "null" } };
        var props = new Dictionary<string, object>
        {
            ["passenger_count"] = Nullable("integer"),
            ["passenger_names"] = new { type = new[] { "array", "null" }, items = new { type = "string" } },
            ["passenger_weight_each_kg"] = Nullable("number"),
            ["baggage_total_kg"] = Nullable("number"),
            ["baggage_note"] = Nullable("string"),
            ["cargo_kg"] = Nullable("number"),
            ["cargo_note"] = Nullable("string"),
            ["pickup"] = Nullable("string"),
            ["dropoff"] = Nullable("string"),
            ["pickup_time_local"] = Nullable("string"),
            ["return_time_local"] = Nullable("string"),
            ["price_nok"] = Nullable("number"),
            ["both_agreed"] = Nullable("boolean"),
            ["pilot_declined"] = Nullable("boolean"),
            ["call_is_over"] = Nullable("boolean"),
            ["new_note"] = Nullable("string"),
        };
        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(props),
            ["required"] = JsonSerializer.SerializeToElement(props.Keys.ToArray()),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };
    }

    public async Task<PlanPatch?> ListenAsync(string user, CancellationToken ct)
    {
        var msg = await Client().Messages.Create(new MessageCreateParams
        {
            Model = settings.ScribeModel,
            MaxTokens = 700,
            Thinking = FastThinking(settings.ScribeModel),
            OutputConfig = new OutputConfig { Effort = Effort.Low, Format = new JsonOutputFormat { Schema = PatchSchema } },
            System = Game.Talk.Prompts.ScribeSystem,
            Messages = [new UserMessageParam { Content = user }],
        }, ct);
        string json = string.Concat(msg.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        return ParsePatch(json);
    }

    public static PlanPatch? ParsePatch(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string? S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            double? D(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            bool? B(string k) => r.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
            List<string>? names = r.TryGetProperty("passenger_names", out var n) && n.ValueKind == JsonValueKind.Array
                ? n.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : null;
            return new PlanPatch
            {
                PassengerCount = D("passenger_count") is { } pc ? (int)pc : null,
                PassengerNames = names, PassengerWeightEachKg = D("passenger_weight_each_kg"),
                BaggageTotalKg = D("baggage_total_kg"), BaggageNote = S("baggage_note"),
                CargoKg = D("cargo_kg"), CargoNote = S("cargo_note"),
                Pickup = S("pickup"), Dropoff = S("dropoff"),
                PickupTimeLocal = S("pickup_time_local"), ReturnTimeLocal = S("return_time_local"),
                PriceNok = D("price_nok"), BothAgreed = B("both_agreed"), PilotDeclined = B("pilot_declined"),
                CallIsOver = B("call_is_over"), NewNote = S("new_note"),
            };
        }
        catch (JsonException ex)
        {
            Log.Error("Brain", "The note taker returned something that is not JSON", ex);
            return null;
        }
    }

    private static List<MessageParam> ToMessages(IReadOnlyList<Turn> history)
    {
        var messages = new List<MessageParam>(history.Count);
        foreach (var t in history)
            messages.Add(t.FromPilot ? new UserMessageParam { Content = t.Text } : new AssistantMessageParam { Content = t.Text });
        return messages;
    }

    /// <summary>A sentence the player can act on, instead of a stack trace or a silent character.</summary>
    public static string Explain(Exception ex) => ex switch
    {
        AnthropicUnauthorizedException => L.T("The Anthropic API key was refused. Check it in Settings."),
        AnthropicForbiddenException => L.T("This API key is not allowed to use that model."),
        AnthropicRateLimitException => L.T("Claude is rate limiting this key. Try again in a moment."),
        AnthropicBadRequestException b when b.Message.Contains("credit", StringComparison.OrdinalIgnoreCase) => L.T("The API credits have run out. They refill with your next billing cycle, or add credits in the Console."),
        AnthropicBadRequestException b => L.F("Claude refused the request: {0}", b.Message),
        AnthropicApiException a => L.F("Claude had a problem: {0}", a.Message),
        _ => L.F("Could not reach Claude: {0}", ex.Message),
    };
}
