namespace FlightCompanion.Game.People;

public sealed record MemoryNote(DateTimeOffset When, string Text);

/// <summary>Someone in the world. They persist for the whole career and remember how you treated them.</summary>
public sealed class Person
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "";
    public string Nationality { get; set; } = "";
    /// <summary>Where they are from, which decides their name and how they talk.</summary>
    public string Home { get; set; } = "";
    /// <summary>"en" or "no": what they speak first.</summary>
    public string Language { get; set; } = "en";
    public bool SpeaksNorwegian { get; set; }
    public string Gender { get; set; } = "male";
    public int Age { get; set; } = 45;
    public string Job { get; set; } = "";
    public string Personality { get; set; } = "";
    public string SpeechStyle { get; set; } = "";
    public double WeightKg { get; set; } = 82;
    /// <summary>0 relaxed, 1 terrified of small aircraft.</summary>
    public double Fear { get; set; } = 0.2;
    /// <summary>0 silent, 1 never stops talking.</summary>
    public double Chatty { get; set; } = 0.5;
    /// <summary>0 lies about everything, 1 scrupulously truthful about weights and plans.</summary>
    public double Honesty { get; set; } = 0.7;
    /// <summary>How easily they part with money, 0 to 1.</summary>
    public double Wealth { get; set; } = 0.5;
    /// <summary>-100 hates you, 100 trusts you with their life.</summary>
    public double Trust { get; set; }
    public int FlightsWithYou { get; set; }
    public string? VoiceId { get; set; }
    public string? ReferredBy { get; set; }
    public string? ArcId { get; set; }
    public List<MemoryNote> Memories { get; set; } = [];
    public DateTimeOffset? LastContact { get; set; }

    public string DescribeForPrompt()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{Name}, {Age}, {(Home.Length > 0 ? "from " + Home : Nationality)}, {Job}. {Personality}");
        if (!string.IsNullOrEmpty(SpeechStyle)) sb.Append($" How you talk: {SpeechStyle}");
        sb.Append(Fear switch { > 0.7 => " You are frightened of small planes.", > 0.4 => " Small planes make you a bit nervous.", _ => " Flying does not bother you." });
        return sb.ToString();
    }
}

public static class CastFactory
{
    private sealed record Origin(string Nationality, string Lang, bool Norwegian, string[] Male, string[] Female, string[] Surnames, string Speech, string Home = "");

    /// <summary>True when everyone in the game speaks Norwegian: then the people are from Norway too, not Japanese tourists speaking Norwegian.</summary>
    public static bool LocalsOnly { get; set; }

    private static readonly string[] NoFromOslo = ["local reindeer herder", "village nurse"];

    private static readonly Origin[] Origins =
    [
        new("German", "en", false, ["Klaus", "Jürgen", "Stefan", "Matthias", "Uwe", "Tobias"], ["Sabine", "Heike", "Anke", "Katrin"], ["Vogel", "Hartmann", "Becker", "Lorenz", "Krause"], "Careful, slightly formal English with a German word slipping in now and then."),
        new("Dutch", "en", false, ["Joost", "Bram", "Pieter", "Ruud"], ["Anouk", "Marieke", "Femke"], ["de Vries", "Bakker", "van Dijk", "Smit"], "Direct and blunt English, says exactly what they think."),
        new("American", "en", false, ["Chuck", "Doug", "Brandon", "Walt", "Tyler"], ["Linda", "Megan", "Carol", "Jess"], ["Miller", "Sullivan", "Hayes", "Cooper"], "Loud, friendly, everything is awesome, asks a lot of questions."),
        new("British", "en", false, ["Nigel", "Graham", "Colin", "Simon"], ["Fiona", "Judith", "Hannah"], ["Whitfield", "Barker", "Ellis", "Thornton"], "Dry understatement, apologises constantly, jokes when nervous."),
        new("Italian", "en", false, ["Marco", "Giorgio", "Luca"], ["Giulia", "Francesca"], ["Rossi", "Conti", "Moretti"], "Warm, expressive English with a strong rhythm, complains about the cold."),
        new("Japanese", "en", false, ["Kenji", "Hiroshi"], ["Yuki", "Aiko"], ["Tanaka", "Sato", "Mori"], "Very polite, short careful sentences, photographs everything."),
        new("Finnish", "en", false, ["Jari", "Pekka", "Mikko"], ["Aino", "Tuula"], ["Korhonen", "Virtanen", "Mäkelä"], "Very few words, long pauses, dry as a bone."),
        new("Norwegian", "no", true, ["Anders", "Henrik", "Erik", "Magnus", "Kristian", "Thomas", "Jonas", "Lars", "Martin", "Fredrik"], ["Ingrid", "Kristin", "Camilla", "Silje", "Ida", "Nora", "Hanne", "Maria", "Line"], ["Hansen", "Johansen", "Olsen", "Larsen", "Andersen", "Berg", "Haugen", "Bakken", "Lie", "Moe"], "Oslo speech, urban East Norwegian, says liksom and ikke sant. Excited to be this far north, a bit out of place, thinks everything here is exotic.", "Oslo"),
        new("Norwegian", "no", true, ["Odd", "Terje", "Roger", "Kjell", "Arne", "Svein", "Ronny", "Geir", "Rune", "Stian"], ["Marit", "Hege", "Tone", "Anne-Lise", "Gunn", "Kari", "Bente", "Torill"], ["Mathisen", "Nilsen", "Pedersen", "Isaksen", "Karlsen", "Henriksen", "Mikkelsen", "Hansen", "Johansen", "Jakobsen"], "Finnmark dialect. Say æ instead of jeg, ka instead of hva, kor instead of hvor, kem instead of hvem, and ikke sant. Relaxed, blunt and funny, swears casually the way people do up north.", "Finnmark"),
        new("Sami", "no", true, ["Aslak", "Nils", "Johan", "Mikkel", "Ánte", "Per", "Isak", "Mathis", "Jon"], ["Ellen", "Inga", "Risten", "Elle", "Berit", "Sara", "Máret"], ["Somby", "Hætta", "Eira", "Sara", "Utsi", "Guttorm", "Bær", "Balto", "Gaup", "Anti", "Turi", "Buljo"], "Calm northern Norwegian. Say æ instead of jeg and ka instead of hva. Knows the land far better than you, few words, sly humour, and a Sami word like bures for hello now and then.", "Sápmi"),
    ];

    private static readonly (string job, string personality, double fear, double chatty, double honesty, double wealth)[] Types =
    [
        ("retired dentist on the trip of a lifetime", "Generous and easily delighted, tends to overpack.", 0.4, 0.8, 0.8, 0.8),
        ("salmon angler obsessed with one particular river", "Single minded, will trade comfort for fishing time, hides how much gear they carry.", 0.1, 0.6, 0.4, 0.6),
        ("wildlife photographer", "Patient, wants low and slow passes, carries heavy cases and insists they cannot be left.", 0.1, 0.5, 0.5, 0.5),
        ("backpacker on a tight budget", "Cheerful, haggles over everything, travels light but owns one absurdly heavy thing.", 0.3, 0.7, 0.6, 0.1),
        ("corporate executive on a team trip", "Used to getting their way, in a hurry, pays well but has opinions on everything.", 0.2, 0.5, 0.6, 0.9),
        ("biologist counting reindeer and birds", "Precise, quotes numbers, a little absent minded about time.", 0.1, 0.6, 0.9, 0.4),
        ("newly married honeymooner", "Romantic and giddy, wants everything to be perfect.", 0.5, 0.8, 0.7, 0.5),
        ("grandparent visiting family up north", "Sweet but stubborn, brings food for everyone, terrified of takeoffs.", 0.8, 0.9, 0.9, 0.3),
        ("hunter after ptarmigan", "Laconic, practical, knows weight matters but still rounds down.", 0.0, 0.3, 0.6, 0.5),
        ("travel blogger", "Constantly filming and narrating, wants dramatic moments, fibs a little for the story.", 0.2, 1.0, 0.4, 0.3),
        ("local reindeer herder", "Has flown with everyone, unimpressed, does not book ahead and does not do small talk.", 0.0, 0.2, 0.9, 0.4),
        ("village nurse", "Calm under pressure, always working, grateful.", 0.2, 0.5, 1.0, 0.3),
        ("geology student", "Curious about the terrain, asks how everything works, carries rock samples.", 0.2, 0.8, 0.8, 0.2),
        ("retired fighter pilot", "Backseat flies you, tells stories, watches your every move.", 0.0, 0.7, 0.8, 0.6),
    ];

    public static Person Create(Random rng, string? nationality = null, string? job = null, string? home = null)
    {
        var pool = nationality is not null
            ? Origins.Where(x => x.Nationality.Equals(nationality, StringComparison.OrdinalIgnoreCase)).ToArray()
            : LocalsOnly ? Origins.Where(x => x.Lang == "no").ToArray() : Origins;
        if (home is not null && pool.Any(x => x.Home == home)) pool = pool.Where(x => x.Home == home).ToArray();
        var o = pool.Length > 0 ? pool[rng.Next(pool.Length)] : Origins[0];
        bool male = rng.NextDouble() < 0.55;
        var t = job is null ? default : Types.FirstOrDefault(x => x.job.Contains(job, StringComparison.OrdinalIgnoreCase));
        if (t == default)
        {
            var fits = Types.Where(x => o.Home != "Oslo" || !NoFromOslo.Contains(x.job)).ToArray();
            t = fits[rng.Next(fits.Length)];
        }
        string first = male ? o.Male[rng.Next(o.Male.Length)] : o.Female[rng.Next(o.Female.Length)];
        double mean = male ? 86 : 70;
        return new Person
        {
            Name = $"{first} {o.Surnames[rng.Next(o.Surnames.Length)]}",
            Nationality = o.Nationality, Home = o.Home, Language = o.Lang, SpeaksNorwegian = o.Norwegian,
            Gender = male ? "male" : "female",
            Age = 24 + rng.Next(50),
            Job = t.job, Personality = t.personality, SpeechStyle = o.Speech,
            WeightKg = Math.Round(mean + Gauss(rng) * 12),
            Fear = Math.Clamp(t.fear + (rng.NextDouble() - 0.5) * 0.3, 0, 1),
            Chatty = Math.Clamp(t.chatty + (rng.NextDouble() - 0.5) * 0.3, 0, 1),
            Honesty = Math.Clamp(t.honesty + (rng.NextDouble() - 0.5) * 0.3, 0, 1),
            Wealth = Math.Clamp(t.wealth + (rng.NextDouble() - 0.5) * 0.2, 0, 1),
        };
    }

    /// <summary>A travel companion, same origin, independent in everything else.</summary>
    public static Person Companion(Random rng, Person lead)
    {
        var p = Create(rng, lead.Nationality.Length > 0 ? lead.Nationality : null, null, lead.Home.Length > 0 ? lead.Home : null);
        var surname = lead.Name.Contains(' ') ? lead.Name[(lead.Name.IndexOf(' ') + 1)..] : "";
        if (rng.NextDouble() < 0.6 && surname.Length > 0)
            p.Name = p.Name.Split(' ')[0] + " " + surname;
        return p;
    }

    /// <summary>
    /// People already created before the names changed, whom you have never flown, become locals. Keeps who they are in the
    /// game (their id, their bookings), and changes their name, background and voice.
    /// </summary>
    public static void MakeLocal(Person p, Random rng)
    {
        if (p.Language == "no" || p.FlightsWithYou > 0 || p.ArcId is not null) return;
        var n = Create(rng);
        p.Name = n.Name; p.Nationality = n.Nationality; p.Home = n.Home; p.Language = n.Language; p.SpeaksNorwegian = n.SpeaksNorwegian;
        p.Gender = n.Gender; p.SpeechStyle = n.SpeechStyle; p.Job = n.Job; p.Personality = n.Personality; p.VoiceId = null;
    }

    private static double Gauss(Random r)
    {
        double u1 = 1 - r.NextDouble(), u2 = r.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
