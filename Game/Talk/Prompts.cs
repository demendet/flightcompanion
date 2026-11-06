using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.Career;
using FlightCompanion.Game.Flight;
using FlightCompanion.Game.Missions;
using FlightCompanion.Game.People;
using FlightCompanion.Game.World;

namespace FlightCompanion.Game.Talk;

public enum TalkMode { Phone, InPerson, Intercom, Debrief }

/// <summary>Who is speaking, and the rules of the scene. Stable through a call, so the API can cache it.</summary>
public static class Prompts
{
    /// <param name="talkLanguage">"no": everyone speaks Norwegian. "en": everyone speaks English. "mixed": as in real life.</param>
    public static string Speaker(Person who, Mission? m, CareerState c, TalkMode mode, string? reason, string talkLanguage = "mixed")
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"""
            You are playing {who.Name}, a real person in a realistic voice drama. The other speaker is a real human, the bush pilot {c.PilotName} of {c.CompanyName}, flying a small Cessna 172 in Finnmark, Arctic Norway. Your words go straight to a voice synthesizer and are heard live.

            HOW TO TALK
            - Say one short sentence, two at most, about twenty words in all, like a real phone call. Ask one thing at a time, then stop and let the pilot talk.
            - Plain speech only. No lists, no stage directions, no asterisks, no brackets, no narration.
            - Use natural spoken language, sometimes hesitating or breaking off. Be a specific person, with moods and opinions, not a helpful assistant.
            - Never say you are an AI or a character. You do not know about games or simulators. To you this is simply real life.
            - If the pilot's words seem garbled, ask what they said, as you would on a bad line.
            - You may push back, haggle, get annoyed, and change your mind. If the pilot gives a good reason, like weight, fuel, safety or weather, you can give in, grudgingly if it suits you. The pilot has the final word on safety.
            - You do not know the aircraft's weight limits or fuel figures. The pilot does. Never make up technical facts.
            """);
        sb.AppendLine("WHO YOU ARE");
        sb.AppendLine(who.DescribeForPrompt());
        if (talkLanguage == "no")
        {
            sb.AppendLine("LANGUAGE: every word you say is in Norwegian bokmal, always, and never in English, whatever the pilot says.");
            sb.AppendLine("Write numbers, weights, prices and times out as words, so a voice can read them: to hundre kilo, halv tre, fem tusen kroner.");
            sb.AppendLine(who.Language == "no"
                ? "It is your own language. Talk exactly the way people from your home talk, as described above, spelled the way they say it."
                : $"You are {who.Nationality} but you have learned Norwegian, simple and a bit imperfect, with a word from home slipping in now and then. Where the description above mentions English, apply that manner to your Norwegian. Do not apologise for it.");
        }
        else if (talkLanguage == "en")
            sb.AppendLine("LANGUAGE: every word you say is in English, whatever the pilot says. Write numbers and times out as words so a voice can read them.");
        else if (who.Language == "no")
            sb.AppendLine("You speak Norwegian bokmal as your own language. If the pilot speaks English you can switch to English, reluctantly.");
        else if (who.SpeaksNorwegian)
            sb.AppendLine("You speak English and Norwegian. Answer in the language the pilot uses.");
        else
            sb.AppendLine("You speak English, not Norwegian. If the pilot speaks Norwegian, say you do not understand it.");

        if (who.FlightsWithYou > 0 || who.Memories.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"YOUR HISTORY WITH THIS PILOT: you have flown together {who.FlightsWithYou} times. Your feeling about them on a scale from -100 to 100 is {who.Trust:0}.");
            foreach (var mem in who.Memories.TakeLast(6))
                sb.AppendLine($"- {Clock.ToLocal(mem.When):d MMM}: {mem.Text}");
            sb.AppendLine("Bring this up naturally if it fits. Real people remember the time you scared them.");
        }
        else if (who.ReferredBy is not null)
            sb.AppendLine($"\n{who.ReferredBy} recommended this pilot to you, and you say so.");

        sb.AppendLine();
        sb.AppendLine("THE PLACE: " + string.Join(" ", WorldData.Places.Where(p => p.Remote).Select(p => $"{p.Name}: {p.Brief}")));

        if (m is not null)
        {
            sb.AppendLine();
            sb.AppendLine("WHAT THIS IS ABOUT");
            sb.AppendLine(m.Premise);
            sb.AppendLine($"Route you have in mind: {m.Pickup?.Name} to {m.Dropoff?.Name}.");
            if (m.Truth.ClaimedPassengers > 0)
                sb.AppendLine($"You are {m.Truth.ClaimedPassengers} people. On the phone you believe your baggage is around {m.Truth.ClaimedBaggageKg:0} kg in total.");
            if (m.Truth.CargoKg > 0) sb.AppendLine($"The cargo is {m.Plan.CargoNote}, around {m.Truth.CargoKg:0} kg.");
            if (m.CompanionIds.Count > 0)
                sb.AppendLine("Travelling with you: " + string.Join(", ", m.CompanionIds.Select(id => c.PersonById(id)?.Name).Where(n => n is not null)) + ".");
            if (m.SecretsForCaller.Count > 0)
            {
                sb.AppendLine("THINGS YOU KNOW AND THE PILOT DOES NOT (reveal them only as described, and never read them out):");
                foreach (var s in m.SecretsForCaller) sb.AppendLine("- " + s);
            }
        }

        sb.AppendLine();
        sb.AppendLine(mode switch
        {
            TalkMode.Phone => reason is not null
                ? $"THE SCENE: you are calling the pilot on the phone. Why: {reason} Open with a short natural line, then let the pilot answer."
                : "THE SCENE: you are calling the pilot on the phone to arrange this. Greet them, say who you are and what you need in one or two sentences, then stop and let them answer. Do not explain everything at once. Agree on people, baggage, the date and time, where to meet, and a price in Norwegian kroner, and accept the kind of number a bush pilot would ask. When it is settled, or the pilot says no, say goodbye and keep it short.",
            TalkMode.InPerson => "THE SCENE: the pilot has landed and walked over to you. You are standing beside the strip in the cold, face to face, with your bags. This is not a phone call any more, so talk as you would to someone in front of you. If you have something to confess, this is the moment.",
            TalkMode.Intercom => "THE SCENE: you are in the aircraft, in flight, wearing a headset over the engine noise. Keep it short, the way people talk on an intercom. React to what happens. You can see out of the window. If a [what just happened] note appears, react to it naturally in a few words, like a person would.",
            _ => "THE SCENE: you have just landed. You are getting out of the aircraft and saying goodbye to the pilot. Mention how the flight felt to you. Settle anything still open, and if it is natural, agree on when they come back for you.",
        });
        return sb.ToString();
    }

    /// <summary>Volatile facts, sent with each turn so the cached part above stays stable.</summary>
    public static string Situation(DateTimeOffset now, Mission? m, Telemetry? t, double comfort, string? weather, string? sight)
    {
        var parts = new List<string> { $"It is {Clock.ToLocal(now):dddd d MMMM HH:mm}." };
        if (m?.Plan is { } p)
        {
            if (p.PickupTime is { } when) parts.Add($"Pickup agreed for {Clock.ToLocal(when):dddd d MMMM HH:mm}.");
            if (p.PricePerPersonNok is { } each) parts.Add($"Price so far {each:0} kroner per person, {p.PriceNok:0} kroner in all.");
            else if (p.PriceNok is { } price) parts.Add($"Price so far {price:0} kroner for the whole trip.");
            if (p.BaggageWeighed) parts.Add($"The bags were weighed: {p.BaggageKg:0} kg.");
        }
        if (weather is not null) parts.Add($"What your weather app says about today: {weather}.");
        if (t is { OnGround: false })
        {
            parts.Add($"You are flying at {t.AglFt:0} feet above ground, {t.GroundSpeedKt:0} knots.");
            parts.Add(comfort switch { < 30 => "You are genuinely frightened.", < 60 => "You are uneasy.", > 90 => "You are relaxed and enjoying it.", _ => "" });
        }
        if (sight is not null) parts.Add($"Below you: {sight}.");
        return string.Join(" ", parts.Where(x => x.Length > 0));
    }

    public const string ScribeSystem = """
        You keep notes for a bush pilot on a live conversation with a customer. After each exchange you return what has been said so far as JSON, for the pilot's planning card.

        Rules:
        - Only record what was actually said. If something was not mentioned, return null for that field. Never assume or estimate.
        - Later statements replace earlier ones. If the pilot and the customer settled on a new number, return the new number.
        - baggage_total_kg is the total for everyone. If they say "20 kilos each" and there are 3 of them, return 60.
        - passenger_count is everyone flying except the pilot.
        - pickup and dropoff are place names as spoken. Return null if none was named.
        - pickup_time_local and return_time_local are ISO 8601 without a time zone, for example 2026-11-08T14:00:00. Work out dates such as "tomorrow" or "Saturday" from the current time given below. Return null if no time was said.
        - The price is the amount agreed or the latest offer in Norwegian kroner. If it was said per person ("3500 per person", "3500 kroner hver", "3500 per snute"), put it in price_per_person_nok and leave price_nok null. If it was for the whole trip or for all of them ("3500 for alle", "3500 for hele turen"), put it in price_nok and leave price_per_person_nok null. If it is unclear, use what the last speaker most plausibly meant. Numbers may be written as words or as "3.5k".
        - both_agreed is true only once the pilot and the customer have clearly both accepted the booking, false if they are still discussing.
        - pilot_declined is true only if the pilot refused the job and that stands.
        - call_is_over is true only if both have said goodbye or are clearly finished.
        - new_note is one short sentence about a special need that matters to the pilot, otherwise null.
        """;

    public static string ScribeUser(DateTimeOffset now, MissionPlan plan, IEnumerable<string> transcript) => $"""
        Current time: {Clock.ToLocal(now):yyyy-MM-ddTHH:mm:ss} ({Clock.ToLocal(now):dddd}).
        Current card: {plan.Passengers.Count} passengers, baggage {plan.BaggageKg:0} kg, cargo {plan.CargoKg:0} kg, pickup {WorldData.ById(plan.PickupId)?.Name ?? "unknown"}, dropoff {WorldData.ById(plan.DropoffId)?.Name ?? "unknown"}, time {(plan.PickupTime is { } t ? Clock.ToLocal(t).ToString("yyyy-MM-ddTHH:mm") : "unknown")}, price {(plan.PricePerPersonNok is { } each ? each.ToString("0") + " NOK per person" : (plan.PriceNok?.ToString("0") ?? "unknown") + " NOK for the whole trip")}.

        Conversation:
        {string.Join("\n", transcript)}
        """;

    public static string MemorySystem(Person who) => $"""
        You are writing one private memory for {who.Name} after a flight with a bush pilot. Write one short plain sentence in English, referring to {who.Name} as "you", about what you will remember about this pilot and this flight, for example: "The pilot flew you through the snow shower and you were sick with fear, but landed like a feather." Return only that sentence.
        """;

    public static string QuoteSystem(Person who, string talkLanguage = "mixed") => $"""
        You are {who.DescribeForPrompt()} You just flew with a bush pilot. Write a short honest review line, as you would say it to a friend, in your voice, in {(talkLanguage == "no" || (talkLanguage != "en" && who.Language == "no") ? "Norwegian" : "English")}. One sentence. Return only that sentence.
        """;

    public static string FlightFacts(Mission m, FlightReview r, IEnumerable<string> tail) => $"""
        Facts about the flight: {string.Join("; ", r.Points)}. Stars out of 5: {r.Stars:0.0}. Route {m.Pickup?.Name} to {m.Dropoff?.Name}.
        Last things said:
        {string.Join("\n", tail)}
        """;
}

/// <summary>Instant reactions, ready before anything can reach the network. A contextual line follows from the AI when it matters.</summary>
public static class ReactionLines
{
    private static readonly Dictionary<FlightEventKind, (string[] en, string[] no)> Lines = new()
    {
        [FlightEventKind.StallWarning] = (["What's that noise?", "Is that alarm normal?", "Oh God, what was that?", "Hey, what's beeping?"], ["Hva er den lyden?", "Er det normalt?", "Herregud, hva var det?"]),
        [FlightEventKind.SteepBank] = (["Whoa, okay.", "Oh wow, that's steep.", "Easy there!"], ["Oi, oi.", "Det var bratt.", "Rolig nå!"]),
        [FlightEventKind.ExtremeBank] = (["Whoa whoa whoa!", "Jesus Christ!", "Are you serious?!"], ["Herregud!", "Hold da kjeft!", "Er du gal?!"]),
        [FlightEventKind.HighG] = (["Oof.", "Ugh, my stomach.", "Whoa!"], ["Uff.", "Å fy.", "Oi!"]),
        [FlightEventKind.NegativeG] = (["Whoa, my stomach!", "Oh no no no.", "Wow!"], ["Oi, magen!", "Nei, nei, nei.", "Hjelp!"]),
        [FlightEventKind.DiveHard] = (["Why are we going down so fast?", "Hey, hey, hey!", "Oh my God."], ["Hvorfor går vi så fort ned?", "Hei, hei, hei!", "Herregud."]),
        [FlightEventKind.LowPass] = (["That's really low!", "Wow, look at that!", "Are we supposed to be this low?"], ["Det er veldig lavt!", "Se der da!", "Skal vi være så lavt?"]),
        [FlightEventKind.Turbulence] = (["Bumpy, huh?", "Whoa, is this normal?", "Ugh."], ["Litt humpete, eller?", "Er dette normalt?", "Uff."]),
        [FlightEventKind.EngineStoppedInAir] = (["Why did it go quiet?!", "The engine! The engine!", "Oh God. Oh God."], ["Hvorfor ble det stille?!", "Motoren! Motoren!", "Herregud."]),
        [FlightEventKind.Overspeed] = (["What's that warning?", "Is that okay?"], ["Hva er det som varsler?", "Går det bra?"]),
    };

    public static IReadOnlyList<string> For(FlightEventKind kind, string language)
    {
        if (!Lines.TryGetValue(kind, out var l)) return [];
        return language == "no" ? l.no : l.en;
    }

    public static IEnumerable<(FlightEventKind Kind, string Line)> All(string language)
    {
        foreach (var kv in Lines)
            foreach (var s in language == "no" ? kv.Value.no : kv.Value.en)
                yield return (kv.Key, s);
    }

    /// <summary>A short acknowledgement, played while the real reply is on its way.</summary>
    public static readonly string[] FillersEn = ["Mm.", "Yeah.", "Okay.", "Right.", "Hm."];
    public static readonly string[] FillersNo = ["Mm.", "Ja.", "Okei.", "Ja, ja.", "Hm."];
}
