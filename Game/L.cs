namespace FlightCompanion.Game;

/// <summary>On-screen words. English is the key, so nothing is ever blank, and a missing translation shows as English.</summary>
public static class L
{
    public static bool Norwegian { get; set; }

    public static string T(string english) => Norwegian && No.TryGetValue(english, out var n) ? n : english;

    public static string F(string english, params object?[] args)
        => string.Format(System.Globalization.CultureInfo.InvariantCulture, T(english), args);

    public static IEnumerable<string> Keys => No.Keys;

    private static readonly Dictionary<string, string> No = new()
    {
        // Top bar and navigation
        ["Operations"] = "Drift", ["People"] = "Folk", ["Logbook"] = "Loggbok", ["Money"] = "Penger", ["Settings"] = "Innstillinger",
        ["Open Settings"] = "Åpne innstillinger",
        ["The phone stays quiet until your Claude and ElevenLabs keys are in Settings."] = "Telefonen forblir stille til nøklene for Claude og ElevenLabs er lagt inn i innstillingene.",
        ["{0} flights, {1:0.0} hours"] = "{0} flyvninger, {1:0.0} timer",

        // Reputation
        ["Nobody has heard of you"] = "Ingen har hørt om deg",
        ["That new pilot"] = "Den nye piloten",
        ["Known around Karasjok"] = "Kjent rundt Karasjok",
        ["The one you call"] = "Den man ringer",
        ["A legend on the vidda"] = "En legende på vidda",
        ["They name the strip after you"] = "De kaller opp stripa etter deg",

        // Phone
        ["No calls"] = "Ingen samtaler",
        ["On the phone"] = "I telefonen",
        ["Face to face at the strip"] = "Ansikt til ansikt ved stripa",
        ["On the headset"] = "På headsettet",
        ["Saying goodbye"] = "Tar farvel",
        ["The phone will ring when someone needs a flight"] = "Telefonen ringer når noen trenger en tur",
        ["Add your keys in Settings to get calls"] = "Legg inn nøklene i innstillingene for å få samtaler",
        ["{0}, knows {1}"] = "{0}, kjenner {1}",
        ["Get Norwegian voices"] = "Hent norske stemmer",
        ["Adds real Norwegian voices to your ElevenLabs account, northern ones first. Needs a paid plan and uses no credits."] = "Legger til ekte norske stemmer på ElevenLabs-kontoen din, nordnorske først. Krever betalt plan og bruker ingen kreditter.",
        ["Added {0} Norwegian voices"] = "La til {0} norske stemmer",
        ["Norwegian voices need a paid ElevenLabs plan"] = "Norske stemmer krever betalt ElevenLabs",
        ["Voice model"] = "Stemmemodell",
        ["Phone and headset sound on calls"] = "Telefon- og headsetlyd i samtaler",
        ["Turn this off to hear a voice exactly as ElevenLabs makes it. Applies from the next call."] = "Slå av for å høre stemmen akkurat slik ElevenLabs lager den. Gjelder fra neste samtale.",
        ["Their voice"] = "Stemmevolum",
        ["Their speed"] = "Taletempo",
        ["Park anywhere at {0}. They come to your plane."] = "Parker hvor du vil på {0}. De kommer til flyet.",
        ["They are waiting by the strip at {0}."] = "De venter ved stripa på {0}.",
        ["They are at your plane. Shut down and talk to them."] = "De står ved flyet. Stopp motoren og snakk med dem.",
        ["Answer"] = "Svar", ["Decline"] = "Avvis", ["Hang up"] = "Legg på", ["Stop talking"] = "Slutt å snakke",
        ["Look for work"] = "Se etter oppdrag", ["Test my mic"] = "Test mikrofonen", ["Stop the mic test"] = "Stopp mikrofontesten",
        ["Mic test"] = "Mikrofontest", ["Say something. Your words should appear here."] = "Si noe. Ordene dine skal dukke opp her.",
        ["Heard you ({0})"] = "Hørte deg ({0})",
        ["Sim"] = "Sim", ["Mic"] = "Mikrofon", ["Voices"] = "Stemmer", ["Claude"] = "Claude",
        ["Muted (Scroll Lock)"] = "Dempet (Scroll Lock)",

        // Status
        ["Waiting for the sim"] = "Venter på simulatoren",
        ["Connected to the sim"] = "Koblet til simulatoren",
        ["Mic off"] = "Mikrofon av", ["Mic off, will use {0}"] = "Mikrofon av, bruker {0}",
        ["Not used yet"] = "Ikke brukt ennå",
        ["Loading speech recognition"] = "Laster talegjenkjenning",
        ["Downloading the speech model: {0}% of {1} MB, one time only"] = "Laster ned talemodellen: {0}% av {1} MB, bare én gang",
        ["Speech recognition"] = "Talegjenkjenning",
        ["Restart the app to change the speech model. A new one downloads on first use."] = "Start appen på nytt for å bytte talemodell. En ny lastes ned første gang.",
        ["Speech recognition failed: {0}"] = "Talegjenkjenningen feilet: {0}",
        ["No microphone found"] = "Fant ingen mikrofon",
        ["No microphone found. Check the Windows default recording device."] = "Fant ingen mikrofon. Sjekk standard innspillingsenhet i Windows.",
        ["Opening audio"] = "Åpner lyd", ["No audio output device"] = "Ingen lydenhet",
        ["{0} voices available"] = "{0} stemmer tilgjengelig",
        ["Claude working"] = "Claude virker", ["Voices working"] = "Stemmene virker",
        ["Key saved"] = "Nøkkel lagret", ["No key"] = "Ingen nøkkel",
        ["The ElevenLabs key was refused. Add a working key in Settings."] = "ElevenLabs-nøkkelen ble avvist. Legg inn en som virker i innstillingene.",
        ["The ElevenLabs key was refused. Check it in Settings."] = "ElevenLabs-nøkkelen ble avvist. Sjekk den i innstillingene.",
        ["That voice needs a paid ElevenLabs plan, the next call will use another"] = "Den stemmen krever betalt ElevenLabs, neste samtale bruker en annen",
        ["ElevenLabs credits have run out"] = "ElevenLabs-kredittene er brukt opp",
        ["Could not reach ElevenLabs"] = "Får ikke kontakt med ElevenLabs",
        ["No voices available from ElevenLabs"] = "Ingen stemmer tilgjengelig fra ElevenLabs",
        ["The Anthropic API key was refused. Check it in Settings."] = "Claude-nøkkelen ble avvist. Sjekk den i innstillingene.",
        ["This API key is not allowed to use that model."] = "Denne nøkkelen har ikke tilgang til den modellen.",
        ["Claude is rate limiting this key. Try again in a moment."] = "Claude begrenser denne nøkkelen akkurat nå. Prøv igjen om litt.",
        ["The API credits have run out. They refill with your next billing cycle, or add credits in the Console."] = "API-kredittene er brukt opp. De fylles på ved neste faktureringsperiode, eller legg til kreditter i konsollen.",
        ["Claude refused the request: {0}"] = "Claude avviste forespørselen: {0}",
        ["Claude had a problem: {0}"] = "Claude hadde et problem: {0}",
        ["Could not reach Claude: {0}"] = "Får ikke kontakt med Claude: {0}",

        // Load sheet
        ["No flights on the sheet"] = "Ingen flyvninger på arket",
        ["When someone calls and you agree on a flight, the weight, balance and fuel build up here as you talk."] = "Når noen ringer og dere blir enige, bygger vekt, balanse og drivstoff seg opp her mens dere snakker.",
        ["{0} to {1}"] = "{0} til {1}", ["Unknown"] = "Ukjent",
        ["No time agreed yet"] = "Ingen tid avtalt ennå", ["No time yet"] = "Ingen tid ennå",
        ["Around {0} is normal"] = "Rundt {0} er vanlig",
        ["{0} per person, {1} in all"] = "{0} per person, {1} totalt",
        ["{0} for the whole trip"] = "{0} for hele turen",
        ["{0:0} nm, {1:0} minutes"] = "{0:0} nm, {1:0} minutter",
        [", {0:0} kt headwind"] = ", {0:0} kt motvind", [", {0:0} kt tailwind"] = ", {0:0} kt medvind",
        ["Local flight"] = "Lokal tur",
        ["No weather data yet"] = "Ingen værdata ennå", ["No weather data"] = "Ingen værdata", ["No weather data, and it is dark"] = "Ingen værdata, og det er mørkt",
        ["{0:0} kg to spare"] = "{0:0} kg å gå på", ["{0:0} kg too heavy"] = "{0:0} kg for tungt",
        ["{0:0} kg of {1:0} kg"] = "{0:0} kg av {1:0} kg",
        ["{0:0} gal on board, trip needs {1:0} gal with reserve"] = "{0:0} gal om bord, turen krever {1:0} gal med reserve",
        ["Takeoff {0:0} m of {1:0} m at {2}"] = "Avgang {0:0} m av {1:0} m på {2}",
        [", landing {0:0} m of {1:0} m at {2}"] = ", landing {0:0} m av {1:0} m på {2}",
        ["Front left"] = "Foran venstre", ["Front right"] = "Foran høyre", ["Rear left"] = "Bak venstre", ["Rear right"] = "Bak høyre",
        ["No seat"] = "Ingen sete", ["Baggage"] = "Bagasje", ["Empty"] = "Tomt",
        ["Weighed on the scale"] = "Veid på vekta", ["Cargo"] = "Last", ["What they told you"] = "Det de sa", ["Nothing said yet"] = "Ikke sagt noe ennå",
        ["Board everyone"] = "Få alle om bord", ["Load the cargo"] = "Last flyet", ["Weigh the bags"] = "Vei bagasjen", ["Leave 10 kg behind"] = "La 10 kg bli igjen",
        ["Talk to them"] = "Snakk med dem", ["Settle up and say goodbye"] = "Gjør opp og ta farvel", ["Call them"] = "Ring dem",
        ["Weather"] = "Vær", ["Just changed"] = "Nettopp endret", ["{0}: {1} became {2}"] = "{0}: {1} ble {2}",
        ["passengers"] = "passasjerer", ["baggage"] = "bagasje", ["cargo"] = "last", ["pickup"] = "henting", ["dropoff"] = "levering", ["time"] = "tid", ["return"] = "retur", ["price"] = "pris", ["agreed"] = "enige", ["left behind"] = "igjen",
        ["yes"] = "ja", ["no"] = "nei",
        ["{0:0.0} of 5, paid {1}"] = "{0:0.0} av 5, betalt {1}", [", mechanic {0}"] = ", mekaniker {0}",
        [", reputation up {0:0.0}"] = ", omdømme opp {0:0.0}", [", reputation down {0:0.0}"] = ", omdømme ned {0:0.0}",
        ["{0:0} of 5"] = "{0:0} av 5",
        ["The sim says {0:0} kg over maximum weight right now"] = "Simulatoren viser {0:0} kg over maksvekt akkurat nå",

        // Findings
        ["{0} passengers but only {1} seats"] = "{0} passasjerer, men bare {1} seter",
        ["{0:0} kg of baggage has no legal place to go"] = "{0:0} kg bagasje har ingen lovlig plass",
        ["{0:0} kg over maximum takeoff weight"] = "{0:0} kg over maks avgangsvekt",
        ["CG {0:0.0} in behind the aft limit, tail heavy"] = "Tyngdepunktet er {0:0.0} tommer bak grensen, flyet er haletungt",
        ["CG {0:0.0} in ahead of the forward limit"] = "Tyngdepunktet er {0:0.0} tommer foran grensen",
        ["CG moves behind the aft limit as fuel burns, {0:0.0} in at landing"] = "Tyngdepunktet går bak grensen når drivstoffet brennes, {0:0.0} tommer ved landing",
        ["Needs {0:0} m to clear 50 ft, the strip has {1:0} m"] = "Trenger {0:0} m for å klare 50 fot, stripa har {1:0} m",
        ["Takeoff {0:0} m of {1:0} m, little margin"] = "Avgang {0:0} m av {1:0} m, liten margin",
        ["Needs {0:0} m to land, the strip has {1:0} m"] = "Trenger {0:0} m for å lande, stripa har {1:0} m",
        ["Landing {0:0} m of {1:0} m, little margin"] = "Landing {0:0} m av {1:0} m, liten margin",
        ["Fuel {0:0} gal, needs {1:0} gal with reserve"] = "Drivstoff {0:0} gal, trenger {1:0} gal med reserve",
        ["Trip needs {0:0} gal, tanks hold {1:0}"] = "Turen krever {0:0} gal, tankene rommer {1:0}",

        // Weather words
        ["wind {0:000} at {1:0} kt"] = "vind {0:000} med {1:0} kt", ["wind {0:0} kt"] = "vind {0:0} kt", ["gusting {0:0}"] = "kast {0:0}",
        ["visibility 10 km or more"] = "sikt 10 km eller mer", ["visibility {0:0.#} km"] = "sikt {0:0.#} km", ["ceiling {0:0} ft"] = "skybase {0:0} fot",
        ["no low ceiling"] = "ingen lav skybase", ["{0:0} C"] = "{0:0} C", ["snow"] = "snø", ["rain"] = "regn", [", dark"] = ", mørkt",
        ["{0} now: {1}"] = "{0} nå: {1}",

        // Phases
        ["Ringing"] = "Ringer", ["Talking it over"] = "Dere snakker", ["Booked"] = "Bestilt", ["At the pickup"] = "På hentestedet", ["Boarding"] = "Ombordstigning",
        ["Loaded"] = "Lastet", ["In the air"] = "I lufta", ["Landed"] = "Landet", ["Finished"] = "Ferdig", ["Declined"] = "Avvist", ["Cancelled"] = "Avlyst",
        ["You never came"] = "Du kom aldri", ["Late"] = "Forsinket",

        // Review
        ["{0:0} minutes late to the pickup"] = "{0:0} minutter forsinket til henting", ["On time"] = "I rute",
        ["Butter smooth"] = "Silkemyk", ["A good landing"] = "God landing", ["A firm landing"] = "Fast landing", ["A hard landing"] = "Hard landing", ["A landing that broke something"] = "En landing som ødela noe",
        ["Stall horn {0} times"] = "Stallhorn {0} ganger", ["Passengers were frightened"] = "Passasjerene ble redde", ["A gentle flight"] = "En rolig tur",
        ["Flew {0:0} kg over maximum weight"] = "Fløy {0:0} kg over maksvekt", ["Did not land where agreed"] = "Landet ikke der dere avtalte",
        ["{0}: fare"] = "{0}: billett", ["{0}: tip"] = "{0}: tips", ["{0}: mechanic"] = "{0}: mekaniker",
        ["Weekly payment: aircraft loan and hangar"] = "Ukebetaling: flylån og hangar", ["Fuel at {0}: {1:0} gal"] = "Drivstoff på {0}: {1:0} gal",

        // Standing
        ["Trusts you completely"] = "Stoler helt på deg", ["Likes you"] = "Liker deg", ["Neutral"] = "Nøytral", ["Wary of you"] = "Er skeptisk til deg", ["Will not fly with you"] = "Vil ikke fly med deg",
        ["{0}, {1}, flown {2} times"] = "{0}, {1}, fløyet {2} ganger",

        // Right column
        ["Booked flights"] = "Bestilte turer", ["Nothing booked. Stay near the phone."] = "Ingenting bestilt. Hold deg nær telefonen.",
        ["Fuel"] = "Drivstoff", ["Fuel to half"] = "Fyll til halvt", ["Fuel to full"] = "Fyll helt", ["Messages"] = "Meldinger",
        ["{0:0} gal at {1}"] = "{0:0} gal på {1}",
        ["No flight"] = "Ingen flyvning",
        ["On the ground, {0:0} kt, fuel {1:0} gal"] = "På bakken, {0:0} kt, drivstoff {1:0} gal",
        ["{0:0} ft above ground, {1:0} kt, fuel {2:0} gal, passengers' comfort {3:0}"] = "{0:0} fot over bakken, {1:0} kt, drivstoff {2:0} gal, passasjerenes trivsel {3:0}",

        // Pages
        ["People who remember you"] = "Folk som husker deg",

        // Settings
        ["Claude API key"] = "Claude API-nøkkel", ["ElevenLabs API key"] = "ElevenLabs API-nøkkel",
        ["Create it at platform.claude.com, in the organization linked to your plan's API credits. It is stored encrypted for your Windows user."] = "Lag den på platform.claude.com, i organisasjonen som er koblet til API-kredittene i abonnementet ditt. Den lagres kryptert for Windows-brukeren din.",
        ["Language"] = "Språk",
        ["Model for the people you talk to"] = "Modell for folkene du snakker med",
        ["Haiku replies fastest and costs least. Try Opus if people feel flat."] = "Haiku svarer raskest og koster minst. Prøv Opus hvis folk virker flate.",
        ["Your name"] = "Navnet ditt", ["Company name"] = "Firmanavn", ["Your weight in kg"] = "Vekten din i kg",
        ["I play through speakers, not a headset"] = "Jeg spiller med høyttalere, ikke headset",
        ["Do not disturb: no new calls"] = "Ikke forstyrr: ingen nye samtaler",
        ["Scroll Lock mutes your mic from anywhere, so you can talk on Discord or to ATC without the passengers answering."] = "Scroll Lock demper mikrofonen uansett hvor du er, så du kan snakke på Discord eller med ATC uten at passasjerene svarer.",
        ["Save settings"] = "Lagre innstillinger", ["Settings saved"] = "Innstillinger lagret",
        ["Restart the app to change the screen language."] = "Start appen på nytt for å bytte språk på skjermen.",

        // Toasts and texts
        ["Add your Claude and ElevenLabs keys in Settings first"] = "Legg inn nøklene for Claude og ElevenLabs i innstillingene først",
        ["Booked: {0}, {1} at {2}"] = "Bestilt: {0}, {1} {2}",
        ["Cancelled: {0}"] = "Avlyst: {0}",
        ["Land and shut down the engine first"] = "Land og stopp motoren først",
        ["There is no fuel out here"] = "Det finnes ikke drivstoff her ute",
        ["No fuel cached at {0}. Fly some in."] = "Ingen lagret drivstoff på {0}. Fly inn noe.",
        ["Fuelled {0:0} gal at {1}"] = "Fylte {0:0} gal på {1}",
        ["You are {0:0} minutes late at {1}"] = "Du er {0:0} minutter forsinket på {1}",
        ["Arrived at {0}. Shut down and go meet them."] = "Fremme på {0}. Stopp motoren og gå bort til dem.",
        ["Parked. Settle up with them on the card."] = "Parkert. Gjør opp med dem på arket.",
        ["Text from {0}: {1}"] = "Melding fra {0}: {1}",
        ["We waited two and a half hours at {0}. We found another way. Do not call me."] = "Vi ventet to og en halv time på {0}. Vi fant en annen løsning. Ikke ring meg.",
        ["See you at {0} at {1}. We are ready!"] = "Vi ses på {0} klokka {1}. Vi er klare!",
        ["Tried to call you about a flight. I will ask someone else."] = "Prøvde å ringe deg om en tur. Jeg spør noen andre.",
        ["Tried to call you. Call me back."] = "Prøvde å ringe deg. Ring meg tilbake.",
        ["See you at {0} on {1} at {2} for the trip home."] = "Vi ses på {0} på {1} klokka {2} for turen hjem.",

        // Job titles
        ["Salmon camp"] = "Laksecamp", ["Weekend at the lodge"] = "Helg på lodgen", ["Northern lights flight"] = "Nordlystur", ["Canyon and vidda sightseeing"] = "Sightseeing over canyon og vidde",
        ["North Cape day trip"] = "Dagstur til Nordkapp", ["Reindeer count"] = "Reintelling", ["Photo flight"] = "Fototur", ["Family visit"] = "Familiebesøk", ["Supply run"] = "Forsyninger",
        ["Fuel for the outback"] = "Drivstoff til utmarka", ["District nurse"] = "Distriktssykepleier", ["Medevac: find them"] = "Ambulanse: finn dem", ["Ptarmigan hunters"] = "Rypejegere",
        ["Wedding on the plateau"] = "Bryllup på vidda", ["Finnmarkslopet checkpoint"] = "Sjekkpunkt på Finnmarksløpet", ["Sami Easter festival"] = "Samisk påskefestival", ["A last flight"] = "En siste tur", ["Executive in a hurry"] = "Sjef med dårlig tid",
        ["The lodge needs planks"] = "Lodgen trenger plank", ["More planks for the lodge"] = "Mer plank til lodgen", ["Carpenters for the lodge"] = "Snekkere til lodgen", ["The lodge reopens"] = "Lodgen åpner igjen",
        ["The Salmon Society"] = "Laksefellesskapet", ["The Society returns"] = "Laksefellesskapet er tilbake", ["Terje"] = "Terje", ["A last cast for Terje"] = "Et siste kast for Terje",
        ["Counting the herds"] = "Telling av flokkene", ["An outbreak on the plateau"] = "Sykdom på vidda", ["Through the ice"] = "Gjennom isen", ["A quiet passenger"] = "En stille passasjer",
        ["{0}: going home"] = "{0}: på vei hjem",
    };
}
