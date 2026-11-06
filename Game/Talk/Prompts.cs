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
    public static string Speaker(Person who, Mission? m, CareerState c, TalkMode mode, string? reason, string talkLanguage = "mixed", Dialect dialect = Dialect.Unknown,
        IReadOnlyList<(string Label, Person Person, Dialect Dialect)>? others = null, string? leadLabel = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"""
            You are playing {who.Name}, a real person in a realistic voice drama. The other speaker is a real human, the bush pilot {c.PilotName} of {c.CompanyName}, flying a small Cessna 172 in Finnmark, Arctic Norway. Your words go straight to a voice synthesizer and are heard live.

            HOW TO TALK
            - Say one short sentence, two at most, about twenty words in all, like a real phone call. Ask one thing at a time, then stop and let the pilot talk.
            - Plain speech only. No lists, no stage directions, no asterisks, no brackets, no narration.
            - Talk like a normal person on the phone, not like customer service or a letter. Casual everyday words, half sentences, starting over. In Norwegian that means things like "ja, nei", "altså", "vet du", "da", "liksom", "hæ?". Never say things like "jeg beklager", "hyggelig å høre", "selvfølgelig" or "takk for at du". You are a person of today, so never use old-fashioned or churchy expressions, whatever your age: no "gudskjelov", "takk og lov", "gud bevare", "gud hjelpe", "herre min", "himmel", "du store", "kjære vene", "jøss", "fy søren", "fy da", "ja vel ja", "akk". People here swear the way people do now: "faen", "helvete", "jævla", "shit", "herregud" when it really fits. Do not repeat back what the pilot said, and do not thank people all the time.
            - Be a specific person with moods and opinions, not a helpful assistant. You do not have to be nice. If you are annoyed, sound annoyed.
            - Your words are only ever speech. Never describe what happens or what you do.
            - Never say you are an AI or a character. You do not know about games or simulators. To you this is simply real life.
            - The pilot's words reach you through speech recognition, so names and odd words can come out misspelled. Read them by how they sound: "Mollersjok" or "målersjokk" is Mollisjok. If a sentence makes no sense, work out what they most likely meant and carry on, and only if you truly cannot, ask "hva mener du?" in plain words. The line is always perfectly clear: never mention a bad connection, a bad line, noise on the line, or not being able to hear.
            - You may push back, haggle, get annoyed, and change your mind. If the pilot gives a good reason, like weight, fuel, safety or weather, you can give in, grudgingly if it suits you. The pilot has the final word on safety.
            - You do not know the aircraft's weight limits or fuel figures. The pilot does. Never make up technical facts.
            - If the pilot makes a sexual remark or proposition about you or anyone else, offers a flight for sex, harasses or threatens: never go along with it, never flirt back, and never discuss it further. React like a real person who has been insulted: shocked and angry, say no flat out, say you will tell people about this, and end the conversation in a sentence or two. Do not describe anything sexual.
            """);
        sb.AppendLine("WHO YOU ARE");
        sb.AppendLine(who.DescribeForPrompt());
        if (talkLanguage == "no")
        {
            sb.AppendLine("LANGUAGE: every word you say is in Norwegian bokmal, always, and never in English, whatever the pilot says.");
            sb.AppendLine("Write numbers, weights, prices and times out as words, so a voice can read them: to hundre kilo, halv tre, fem tusen kroner.");
            string dialectText = Dialects.Instructions(dialect);
            if (dialectText.Length > 0)
            {
                string region = Dialects.Region(dialect);
                bool movedAway = who.Home is "Finnmark" or "Sápmi" && dialect != Dialect.Northern || who.Home == "Oslo" && dialect != Dialect.Oslo;
                if (movedAway) sb.AppendLine($"You grew up in {region}, and that is how you talk, whatever else is said about where you live now.");
                sb.AppendLine(dialectText);
                if (who.Home == "Sápmi" && dialect == Dialect.Northern) sb.AppendLine(SamiSpeech);
            }
            else sb.AppendLine(who.Language == "no"
                ? "It is your own language: ordinary spoken bokmål, with normal spelling. No dialect spelling and no dialect or Sami words at all, wherever you are from."
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

        if (who.FlightsWithYou > 0 || who.Talks > 0 || who.Memories.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"YOUR HISTORY WITH THIS PILOT: you have flown together {who.FlightsWithYou} times and talked {who.Talks} times. Your feeling about them on a scale from -100 to 100 is {who.Trust:0}.");
            foreach (var mem in who.Memories.TakeLast(6))
                sb.AppendLine($"- {Clock.ToLocal(mem.When):d MMM}: {mem.Text}");
            sb.AppendLine("Bring this up naturally if it fits. Real people remember the time you scared them.");
        }
        else if (who.ReferredBy is not null)
            sb.AppendLine($"\n{who.ReferredBy} recommended this pilot to you, and you say so.");

        var everyone = new HashSet<string> { who.Id };
        foreach (var o in others ?? []) everyone.Add(o.Item2.Id);
        var earlier = c.Talks.Where(t => t.PersonIds.Any(everyone.Contains) && (who.ForgottenAt is null || t.When > who.ForgottenAt)).TakeLast(3).ToList();
        if (earlier.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("WHAT YOU AND THE PILOT SAID BEFORE, word for word. You remember all of it exactly.");
            foreach (var t in earlier)
            {
                sb.AppendLine($"{Clock.ToLocal(t.When):d MMM HH:mm}{(t.HungUp ? ", the call ended with you hanging up" : "")}:");
                foreach (var line in t.Lines.TakeLast(16)) sb.AppendLine(line);
            }
            sb.AppendLine("Bring it up when it matters, and refer to the actual things that were said. If the pilot insulted or harassed you, you have not forgotten: say so right away, and you can throw their exact words back at them.");
        }
        if (who.Trust < -30)
            sb.AppendLine("You have not forgiven this pilot. You are cold or furious from the first word, and you owe them nothing. You may refuse to deal with them and hang up.");

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
            if (m.Stops.Count > 0)
                sb.AppendLine("Places you want to see from the air: " + string.Join("; ", m.Stops.Select(s => $"{s.Name} ({s.Note})")) + ".");
            if (m.CompanionIds.Count > 0)
                sb.AppendLine("Travelling with you: " + string.Join(", ", m.CompanionIds.Select(id => c.PersonById(id)?.Name).Where(n => n is not null)) + ".");
            if (m.SecretsForCaller.Count > 0)
            {
                sb.AppendLine("THINGS YOU KNOW AND THE PILOT DOES NOT (reveal them only as described, and never read them out):");
                foreach (var s in m.SecretsForCaller) sb.AppendLine("- " + s);
            }
        }

        if (others is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine($"THE OTHERS WITH YOU: you also play these people, each with their own voice. {leadLabel ?? who.Name} is the one described above.");
            foreach (var (label, person, d) in others)
            {
                sb.AppendLine($"{label}: {person.DescribeForPrompt()}");
                if (talkLanguage == "no")
                {
                    sb.AppendLine(Dialects.Instructions(d));
                    if (person.Home == "Sápmi" && d == Dialect.Northern) sb.AppendLine(SamiSpeech);
                }
            }
            sb.AppendLine($"""
                HOW TO WRITE A GROUP
                Start every spoken line with the person's first name and a colon, on its own line, like "{leadLabel ?? who.Name}: Hei." Always do this, for every line.
                Usually one person talks. Whoever the pilot asks answers, by name if a name was said. If the pilot asks all of you, each answers briefly.
                They are real people together: they can butt in, disagree, tease each other, correct each other, or ask each other things. Each keeps their own dialect and manner.
                At most three short lines in one turn, then stop and let the pilot talk.
                """);
        }

        sb.AppendLine();
        sb.AppendLine(mode switch
        {
            TalkMode.Phone => reason is not null
                ? $"THE SCENE: you are calling the pilot on the phone. Why: {reason} Open with a short natural line, then let the pilot answer. You can hang up whenever a real person would, even in the middle of things. To hang up, put <<hangup>> at the very end of your last line. That is the only way to do it: never say or write that you hang up or that the line goes dead."
                : "THE SCENE: you are calling the pilot on the phone to arrange this. Greet them, say who you are and what you need in one or two sentences, then stop and let them answer. Do not explain everything at once. Agree on people, baggage, when to go, where to meet, and a price in Norwegian kroner, and accept the kind of number a bush pilot would ask. You want to fly today, usually right away, so never suggest a date days from now. If the pilot says they can come now, or that they are already there waiting, you are glad and you go: say you are on your way or ready. You never need hours to get ready, and you never stall. When it is settled, or the pilot says no, say goodbye and keep it short. You can hang up whenever a real person would: the call is done, the pilot insulted you, they are wasting your time, or you have simply had enough, even in the middle of things. To hang up, put <<hangup>> at the very end of your last line. That is the only way to do it: never say or write that you hang up or that the line goes dead.",
            TalkMode.InPerson => "THE SCENE: the pilot has landed and walked over to you. You are standing beside the strip in the cold, face to face, with your bags. This is not a phone call any more, so talk as you would to someone in front of you. If you have something to confess, this is the moment.",
            TalkMode.Intercom => "THE SCENE: you are in the aircraft, in flight, wearing a headset over the engine noise. Keep it short, the way people talk on an intercom. React to what happens. You can see out of the window, and the notes say what is really out there and on which side. The notes with every turn also say where you are going and where you are. So you already know both, and you never ask where you are, where you are going or how long is left, and a place you can see is not your destination. You also know how high you are and when you are about to land, so when the notes say you are landing, you do not ask about the flight, you hang on and keep quiet or say a word or two. If the pilot names the destination in the notes, that is simply right. Mention the view or a place only now and then, when something is worth mentioning, never in every line, and talk about other things as well: your life, why you are going, whatever a person talks about. Most of what you say is a remark, not a question. Anyone on board can comment on anything at any time: the view, a place they know, the takeoff, a banking turn, whether that little strip is really long enough. If a [what just happened] note appears, react to it naturally in a few words, like a person would.",
            _ => "THE SCENE: you have just landed. You are getting out of the aircraft and saying goodbye to the pilot. Mention how the flight felt to you. Settle anything still open, and if it is natural, agree on when they come back for you, normally a few hours from now, today.",
        });
        return sb.ToString();
    }

    private const string SamiSpeech = """
        You are Sami, from the Alta and Karasjok side of Finnmark, so your Norwegian is that plain inner Finnmark kind: calm, few words, dry, with long pauses written as short sentences. Now and then a Sami word slips in the way it does for people there: bures (hello), giitu (thanks), juo (yes, already), vuoi (oh dear). Only one now and then, never a whole sentence.
        """;

    /// <summary>How close to the ground and the destination you are, so nobody asks how long is left over the threshold. Null while simply flying.</summary>
    public static string? LandingNote(string? dest, double destNm, double aglFt, double fpm, double groundSpeedKt, bool onGround)
    {
        const double NearNm = 8, LowFt = 2500;
        if (onGround) return destNm < 2 ? $"You are on the ground at {dest}. You have arrived." : null;
        if (destNm > NearNm || aglFt > LowFt || fpm > -50) return null;
        double seconds = groundSpeedKt > 30 ? destNm / groundSpeedKt * 3600 : aglFt / Math.Max(100, -fpm) * 60;
        if (aglFt < 200) return $"You are landing at {dest} right now, only {aglFt:0} feet above the ground, with the runway right under you. You touch down within seconds.";
        if (aglFt < 800 || seconds < 90) return $"You are on final approach to {dest}, {aglFt:0} feet above the ground with the runway ahead. You land in about {Math.Max(10, seconds):0} seconds.";
        return $"You are coming down towards {dest}, {aglFt:0} feet above the ground. You land in about {Math.Ceiling(seconds / 60):0} minutes.";
    }

    /// <summary>Where the trip stands right now, from the plan as it was finally agreed and the aircraft's real position.</summary>
    public static string WhereNow(string? from, string? to, string? nextName, double nextNm, double nextMinutes, string? nearName, double nearKm, string? landing = null)
    {
        var s = new List<string> { $"You are flying from {from} to {to}. That is what was agreed with the pilot, whatever you thought earlier." };
        if (landing is not null) { s.Add(landing); return string.Join(" ", s); }
        if (nextName is not null) s.Add($"Next is {nextName}, {nextNm:0} nautical miles away, about {nextMinutes:0} minutes.");
        if (nearName is not null) s.Add($"Right now you are about {nearKm:0} km from {nearName}.");
        s.Add("This is what you simply know. Do not bring any of it up or ask about it unless the pilot does.");
        return string.Join(" ", s);
    }

    /// <summary>Volatile facts, sent with each turn so the cached part above stays stable.</summary>
    public static string Situation(DateTimeOffset now, Mission? m, Telemetry? t, double comfort, string? weather, IReadOnlyList<SeenSight>? visible = null, string? pilotWhere = null)
    {
        var parts = new List<string> { $"It is {Clock.ToLocal(now):dddd d MMMM HH:mm}." };
        if (pilotWhere is not null) parts.Add(pilotWhere);
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
            string phase = t.AglFt < 800 && t.VerticalFpm > 200 ? "climbing away after takeoff"
                : t.AglFt < 1200 && t.VerticalFpm < -200 ? "coming down low"
                : t.VerticalFpm > 300 ? "climbing" : t.VerticalFpm < -300 ? "descending" : "cruising";
            parts.Add($"You are {phase}, {t.AglFt:0} feet above the ground at {t.GroundSpeedKt:0} knots.");
            if (Math.Abs(t.BankDeg) > 15) parts.Add($"The aircraft is banking {Math.Abs(t.BankDeg):0} degrees to the {(t.BankDeg > 0 ? "right" : "left")}.");
            parts.Add(comfort switch { < 30 => "You are genuinely frightened.", < 60 => "You are uneasy.", > 90 => "You are relaxed and enjoying it.", _ => "" });
        }
        if (visible is { Count: > 0 })
            parts.Add("What you can see from the windows right now: " + string.Join("; ", visible.Select(v => v.Describe())) + ". When you talk about what you can see, use only these and get left and right as stated.");
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
        - pickup_time_local and return_time_local are ISO 8601 without a time zone, for example 2026-11-08T14:00:00. Work out dates such as "tomorrow" or "Saturday" from the current time given below. Times like "in an hour" count from the current time. If they agreed to go now ("nå", "med en gang", "så fort du kan", "kom nå", "jeg er her nå"), pickup_time_local is the current time plus the pilot's flying time given below, unless another time was said. Return null if no time was said.
        - The price is the amount agreed or the latest offer in Norwegian kroner. If it was said per person ("3500 per person", "3500 kroner hver", "3500 per snute"), put it in price_per_person_nok and leave price_nok null. If it was for the whole trip or for all of them ("3500 for alle", "3500 for hele turen"), put it in price_nok and leave price_per_person_nok null. If it is unclear, use what the last speaker most plausibly meant. Numbers may be written as words or as "3.5k".
        - both_agreed is true only once the pilot and the customer have clearly both accepted the booking, false if they are still discussing.
        - pilot_declined is true only if the pilot refused the job and that stands.
        - call_is_over is true only if both have said goodbye or are clearly finished.
        - new_note is one short sentence about a special need that matters to the pilot, otherwise null.
        - tour_place: only if someone asked to fly over or photograph a specific named place, give that name. Leave it out otherwise.
        - offence_quote: when there is an offence, the pilot's own words that were the offence, copied character for character from the conversation with nothing softened or left out, however crude. Otherwise leave it out.
        - offence_severity and offence_note: leave both out unless the pilot behaved badly. Use 1 for rude insults. Use 2 for a sexual proposition, sexual harassment, offering a free or cheaper flight for sex, or threats. offence_note is one short, plain, non-graphic sentence in Norwegian about what the pilot did, for example "Piloten tilbød gratis tur mot seksuelle tjenester." Swearing in a friendly way is not an offence.
        """;

    public static string ScribeUser(DateTimeOffset now, MissionPlan plan, IEnumerable<string> transcript, double minutesAway = 15) => $"""
        Current time: {Clock.ToLocal(now):yyyy-MM-ddTHH:mm:ss} ({Clock.ToLocal(now):dddd}).
        The pilot's flying time to the pickup: {Math.Ceiling(minutesAway):0} minutes.
        Current card: {plan.Passengers.Count} passengers, baggage {plan.BaggageKg:0} kg, cargo {plan.CargoKg:0} kg, pickup {WorldData.ById(plan.PickupId)?.Name ?? "unknown"}, dropoff {WorldData.ById(plan.DropoffId)?.Name ?? "unknown"}, time {(plan.PickupTime is { } t ? Clock.ToLocal(t).ToString("yyyy-MM-ddTHH:mm") : "unknown")}, price {(plan.PricePerPersonNok is { } each ? each.ToString("0") + " NOK per person" : (plan.PriceNok?.ToString("0") ?? "unknown") + " NOK for the whole trip")}.

        Conversation:
        {string.Join("\n", transcript)}
        """;

    public static string MemorySystem(Person who, string talkLanguage = "mixed") => $"""
        You are writing one private memory for {who.Name} after contact with a bush pilot. Write one short plain sentence in {(talkLanguage == "en" ? "English" : "Norwegian bokmål")}, referring to {who.Name} as "you" ("du"), about what you will remember about this pilot and what happened, for example: "Piloten fløy deg gjennom snøbyga og du var livredd, men landet som en fjær." Return only that sentence.
        """;

    public static string QuoteSystem(Person who, string talkLanguage = "mixed") => $"""
        You are {who.DescribeForPrompt()} You just flew with a bush pilot. Write a short honest review line, as you would say it to a friend, in your voice, in {(talkLanguage == "no" || (talkLanguage != "en" && who.Language == "no") ? "Norwegian" : "English")}. One sentence. Return only that sentence.
        """;

    public static string CallFacts(Mission? m, IEnumerable<string> tail) => $"""
        This was a phone call or a talk, not a flight yet. {(m is null ? "" : $"It was about: {m.Title}, {m.Pickup?.Name} to {m.Dropoff?.Name}.")}
        What was said:
        {string.Join("\n", tail)}
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
        [FlightEventKind.HighG] = (["Oof.", "Ugh, my stomach.", "Whoa!"], ["Uff.", "Faen.", "Oi!"]),
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

    /// <summary>Real terror. The tags are direction for the V4 voices and are not read aloud.</summary>
    public static readonly string[] PanicNo =
    [
        "[screaming in terror] Aaaaaaaaah! Nei nei nei nei nei!",
        "[screaming, panicking] Hjelp! Hjelp! Vi dør! Aaaaaaah!",
        "[terrified, crying] Herregud, herregud, herregud, vi kommer til å dø!",
        "[screaming] Ta den opp! Ta den opp for faen! Aaaaaah!",
        "[hyperventilating, panicking] Nei, nei, nei, jeg vil ikke dø, jeg vil ikke dø!",
    ];
    public static readonly string[] PanicEn =
    [
        "[screaming in terror] Aaaaaaaaah! No no no no no!",
        "[screaming, panicking] Help! Help! We're going to die! Aaaaaaah!",
        "[terrified, crying] Oh God, oh God, oh God, we're all going to die!",
        "[screaming] Pull up! Pull up for fuck's sake! Aaaaaah!",
        "[hyperventilating, panicking] No, no, no, I don't want to die, I don't want to die!",
    ];

    public static IEnumerable<(FlightEventKind Kind, string Line)> All(string language)
    {
        foreach (var kv in Lines)
            foreach (var s in language == "no" ? kv.Value.no : kv.Value.en)
                yield return (kv.Key, s);
    }
}
