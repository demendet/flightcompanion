using FlightCompanion.Game.Aviation;
using FlightCompanion.Game.Talk;
using Xunit;

namespace FlightCompanion.Tests;

public class SentenceSplitterTests
{
    [Fact]
    public void Sentences_come_out_as_soon_as_the_next_word_starts()
    {
        var s = new SentenceSplitter();
        Assert.Empty(s.Push("Hello the"));
        Assert.Empty(s.Push("re."));
        Assert.Equal(["Hello there."], s.Push(" We need"));
        Assert.Equal("We need", s.Flush());
        Assert.Null(s.Flush());
    }

    [Fact]
    public void Decimals_and_titles_are_not_sentence_ends()
    {
        var s = new SentenceSplitter();
        Assert.Empty(s.Push("It weighs 3.5 kilos and Dr. Hansen says so"));
        Assert.Equal(["It weighs 3.5 kilos and Dr. Hansen says so."], s.Push(". Okay"));
    }

    [Fact]
    public void A_very_long_clause_is_cut_at_a_comma_so_speech_starts_quickly()
    {
        var s = new SentenceSplitter();
        string longClause = new string('a', 120) + ", and more";
        Assert.Single(s.Push(longClause));
    }

    [Theory]
    [InlineData("*laughs* Okay then.", "Okay then.")]
    [InlineData("[nervously] Is that normal?", "Is that normal?")]
    [InlineData("Well (pause) maybe.", "Well maybe.")]
    public void Stage_directions_are_never_read_aloud(string raw, string clean) => Assert.Equal(clean, SpeechText.Clean(raw));

    [Fact]
    public void English_only_people_stay_english_even_if_the_pilot_speaks_norwegian()
    {
        Assert.Equal("en", SpeechText.VoiceLanguage("en", false, "no"));
        Assert.Equal("no", SpeechText.VoiceLanguage("no", true, "no"));
        Assert.Equal("en", SpeechText.VoiceLanguage("no", true, "en"));
        Assert.Equal("no", SpeechText.VoiceLanguage("no", true, null));
    }

    [Fact]
    public void Language_setting_overrides_who_they_are()
    {
        Assert.Equal("no", SpeechText.VoiceLanguage("en", false, "en", "no"));
        Assert.Equal("en", SpeechText.VoiceLanguage("no", true, "no", "en"));
    }

    [Fact]
    public void Every_person_is_told_to_speak_norwegian_when_that_is_the_setting()
    {
        var german = new FlightCompanion.Game.People.Person { Name = "Klaus", Nationality = "German", Language = "en" };
        string prompt = Prompts.Speaker(german, null, new FlightCompanion.Game.Career.CareerState(), TalkMode.Phone, null, "no");
        Assert.Contains("Norwegian bokmal, always", prompt);
        Assert.DoesNotContain("say you do not understand", prompt);
    }

    [Fact]
    public void Translations_keep_their_placeholders()
    {
        FlightCompanion.Game.L.Norwegian = true;
        try
        {
            foreach (var key in FlightCompanion.Game.L.Keys)
            {
                var want = System.Text.RegularExpressions.Regex.Matches(key, @"\{\d").Select(m => m.Value).OrderBy(v => v);
                var got = System.Text.RegularExpressions.Regex.Matches(FlightCompanion.Game.L.T(key), @"\{\d").Select(m => m.Value).OrderBy(v => v);
                Assert.Equal(want, got);
            }
            foreach (var t in FlightCompanion.Game.Missions.MissionCatalog.Templates)
                Assert.NotEqual(t.Title, FlightCompanion.Game.L.T(t.Title));
        }
        finally { FlightCompanion.Game.L.Norwegian = false; }
    }

    [Fact]
    public void Cut_off_speech_records_only_what_was_heard()
    {
        Assert.Equal("one two [cut off]", SpeechText.HeardPart("one two three four", 0.5));
        Assert.Equal("", SpeechText.HeardPart("one two", 0));
        Assert.Equal("one two", SpeechText.HeardPart("one two", 1));
    }
}

public class SpeakerSplitterTests
{
    private static readonly string[] Labels = ["Mathis", "Camilla", "Eirik"];

    private static List<(string, string)> Run(string text, int chunk)
    {
        var s = new SpeakerSplitter(Labels);
        var got = new List<(string, string)>();
        for (int i = 0; i < text.Length; i += chunk) got.AddRange(s.Push(text.Substring(i, Math.Min(chunk, text.Length - i))));
        got.AddRange(s.Flush());
        return got;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(500)]
    public void Each_line_goes_to_its_speaker_however_the_text_streams_in(int chunk)
    {
        var got = Run("Mathis: Bures. Æ e klar.\nCamilla: Hei! Er det trygt?\nEirik: Eg e ikkje imponert.", chunk);
        Assert.Equal([("Mathis", "Bures."), ("Mathis", "Æ e klar."), ("Camilla", "Hei!"), ("Camilla", "Er det trygt?"), ("Eirik", "Eg e ikkje imponert.")], got);
    }

    [Fact]
    public void A_time_is_not_a_name()
    {
        var got = Run("Klokka 14:00 passer bra.", 2);
        Assert.Equal([("Mathis", "Klokka 14:00 passer bra.")], got);
    }

    [Fact]
    public void A_full_name_or_lower_case_still_finds_the_person_and_no_label_means_the_first()
    {
        Assert.Equal("Camilla", Run("Camilla Berg: Hei.", 4)[0].Item1);
        Assert.Equal("Eirik", Run("eirik: Nei.", 4)[0].Item1);
        Assert.Equal("Mathis", Run("Hei du.", 2)[0].Item1);
    }

    [Fact]
    public void An_unknown_label_is_never_read_aloud()
    {
        var got = Run("Alle: Hei!", 3);
        Assert.Equal("Hei!", got[0].Item2);
    }

    [Fact]
    public void Group_prompt_lists_everyone_with_their_own_dialect_and_the_format()
    {
        var c = new FlightCompanion.Game.Career.CareerState();
        var lead = new FlightCompanion.Game.People.Person { Name = "Mathis Guttorm", Home = "Sápmi", Language = "no", SpeaksNorwegian = true };
        var other = new FlightCompanion.Game.People.Person { Name = "Eirik Lie", Language = "no", SpeaksNorwegian = true };
        string p = Prompts.Speaker(lead, null, c, TalkMode.InPerson, null, "no", Dialect.Northern, [("Eirik", other, Dialect.Bergen)], "Mathis");
        Assert.Contains("HOW TO WRITE A GROUP", p);
        Assert.Contains("eg (jeg)", p);
        Assert.Contains("æ (jeg)", p);
        Assert.Contains("bures", p);
    }
}

public class DialectTests
{
    [Theory]
    [InlineData("northern", Dialect.Northern)]
    [InlineData("no-northern", Dialect.Northern)]
    [InlineData("bergen", Dialect.Bergen)]
    [InlineData("trøndersk", Dialect.Trondersk)]
    [InlineData("oslo", Dialect.Oslo)]
    [InlineData("standard", Dialect.Oslo)]
    [InlineData("american", Dialect.Unknown)]
    public void The_voice_accent_decides_the_dialect(string accent, Dialect want) => Assert.Equal(want, Dialects.FromAccent(accent));

    [Fact]
    public void Each_dialect_gets_its_own_words_and_oslo_never_gets_dialect_words()
    {
        var c = new FlightCompanion.Game.Career.CareerState();
        var who = new FlightCompanion.Game.People.Person { Name = "Kari", Home = "Finnmark", Language = "no", SpeaksNorwegian = true };
        Assert.Contains("æ (jeg)", Prompts.Speaker(who, null, c, TalkMode.Phone, null, "no", Dialect.Northern));
        Assert.Contains("eg (jeg)", Prompts.Speaker(who, null, c, TalkMode.Phone, null, "no", Dialect.Bergen));
        Assert.Contains("itj (ikke)", Prompts.Speaker(who, null, c, TalkMode.Phone, null, "no", Dialect.Trondersk));
        Assert.Contains("Never use dialect forms", Prompts.Speaker(who, null, c, TalkMode.Phone, null, "no", Dialect.Oslo));
    }

    [Fact]
    public void A_finnmark_person_with_a_bergen_voice_is_explained_as_having_grown_up_there()
    {
        var c = new FlightCompanion.Game.Career.CareerState();
        var who = new FlightCompanion.Game.People.Person { Name = "Kari", Home = "Finnmark", Language = "no", SpeaksNorwegian = true };
        Assert.Contains("You grew up in Bergen", Prompts.Speaker(who, null, c, TalkMode.Phone, null, "no", Dialect.Bergen));
        Assert.DoesNotContain("You grew up in", Prompts.Speaker(who, null, c, TalkMode.Phone, null, "no", Dialect.Northern));
    }

    [Theory]
    [InlineData("Hva er den lyden?", Dialect.Northern, "Ka e den lyden?")]
    [InlineData("Hva er den lyden?", Dialect.Bergen, "Ka e den lyden?")]
    [InlineData("Hvorfor går vi så fort ned?", Dialect.Northern, "Koffer går vi så fort ned?")]
    [InlineData("Hva er den lyden?", Dialect.Oslo, "Hva er den lyden?")]
    [InlineData("Herregud!", Dialect.Northern, "Herregud!")]
    public void Quick_reactions_follow_the_dialect(string line, Dialect d, string want) => Assert.Equal(want, Dialects.Adapt(line, d));
}

public class HangUpTests
{
    [Theory]
    [InlineData("INGRID HANGS UP. LINE GOES DEAD.", "", true)]
    [InlineData("Ingrid hangs up", "", true)]
    [InlineData("Ingrid legger på.", "", true)]
    [InlineData("Linja går død.", "", false)]
    [InlineData("Ha det. <<hangup>>", "Ha det.", true)]
    [InlineData("Ikke ring meg igjen. [hangup]", "Ikke ring meg igjen.", true)]
    [InlineData("Nå legger jeg på, din tulling.", "Nå legger jeg på, din tulling.", true)]
    [InlineData("Ja, det går bra, jeg står klar.", "Ja, det går bra, jeg står klar.", false)]
    [InlineData("Vi ses på stripa.", "Vi ses på stripa.", false)]
    [InlineData("Jeg legger på fem hundre kroner, jeg.", "Jeg legger på fem hundre kroner, jeg.", false)]
    [InlineData("Du legger på bagasjen selv.", "Du legger på bagasjen selv.", false)]
    public void Hanging_up_is_an_action_and_narration_is_never_read_aloud(string said, string text, bool hang)
    {
        var (t, h) = SpeechText.TakeHangup(said);
        Assert.Equal(text, t);
        Assert.Equal(hang, h);
    }
}

public class NameFixerTests
{
    private static readonly NameFixer Fixer = new(["Mollisjok", "Karasjok", "Honningsvåg", "Lakselv", "Kirkenes", "Alta", "Hammerfest", "Mollisjok lodge", "the Sautso canyon", "Kamilla Hansen", "Per Olsen"]);

    [Theory]   // the first four are word for word from real calls
    [InlineData("Nei, jeg sitter på rullebanen i målersjokk og venter på deg.", "Nei, jeg sitter på rullebanen i Mollisjok og venter på deg.")]
    [InlineData("så blir dere igjen i Mollersjok?", "så blir dere igjen i Mollisjok?")]
    [InlineData("Men jeg kan fly deg fra Alta til Molesjok.", "Men jeg kan fly deg fra Alta til Mollisjok.")]
    [InlineData("Nei, honningsåk det gidder jeg ikke.", "Nei, Honningsvåg det gidder jeg ikke.")]
    [InlineData("Vi flyr til Karasjokk i dag", "Vi flyr til Karasjok i dag")]
    [InlineData("Er det Kamill som kommer?", "Er det Kamilla som kommer?")]
    [InlineData("rett til Mollis jokk nå", "rett til Mollisjok nå")]
    public void Puts_real_names_back(string heard, string expected) => Assert.Equal(expected, Fixer.Fix(heard));

    [Theory]
    [InlineData("Ja, men det går helt bra, vet du.")]
    [InlineData("Jeg sitter på rullebanen og venter på flyplassen.")]
    [InlineData("3500 kroner per person, og laksene er store.")]
    [InlineData("Hold kjeft, de kirkene er gamle, alle sammen.")]
    [InlineData("Har du bagasjen klar, så kommer jeg nå med en gang.")]
    [InlineData("Mollisjok og Karasjok er allerede riktig.")]
    public void Leaves_ordinary_words_alone(string heard) => Assert.Equal(heard, Fixer.Fix(heard));
}

public class SpeechGateTests
{
    private static readonly float[] Frame = new float[SpeechGate.FrameSamples];

    private static List<GateEvent> Run(SpeechGate g, double prob, int frames)
    {
        var ev = new List<GateEvent>();
        for (int i = 0; i < frames; i++)
        {
            var e = g.Process(Frame, prob);
            if (e != GateEvent.None) ev.Add(e);
        }
        return ev;
    }

    [Fact]
    public void Real_speech_starts_confirms_and_ends_with_audio()
    {
        var g = new SpeechGate();
        Run(g, 0.01, 20);
        Assert.Equal([GateEvent.SpeechStarted, GateEvent.SpeechConfirmed], Run(g, 0.9, 30));
        Assert.Equal([GateEvent.SpeechEnded], Run(g, 0.01, 20));
        Assert.NotNull(g.CompletedUtterance);
        Assert.True(g.CompletedUtterance!.Length > 30 * SpeechGate.FrameSamples);   // includes the lead-in
    }

    [Fact]
    public void A_cough_never_confirms_so_it_cannot_cut_anyone_off()
    {
        var g = new SpeechGate();
        var ev = new List<GateEvent>();
        ev.AddRange(Run(g, 0.9, 4));
        ev.AddRange(Run(g, 0.01, 20));
        Assert.DoesNotContain(GateEvent.SpeechConfirmed, ev);
        Assert.DoesNotContain(GateEvent.SpeechEnded, ev);
        Assert.False(g.IsActive);
    }

    [Fact]
    public void Short_pause_between_words_does_not_end_the_turn()
    {
        var g = new SpeechGate();
        Run(g, 0.9, 30);
        Assert.Empty(Run(g, 0.01, 8));   // 256 ms of breath
        Assert.Empty(Run(g, 0.9, 10));
        Assert.Equal([GateEvent.SpeechEnded], Run(g, 0.01, 16));
    }

    [Fact]
    public void Continuous_noise_is_forced_to_end_rather_than_listening_forever()
    {
        var g = new SpeechGate();
        Assert.Contains(GateEvent.SpeechEnded, Run(g, 0.9, 31 * 31));
    }
}

public class StationMapperTests
{
    // Exactly what the sim reported for "C172SP G1000 Passengers" on 2026-11-06.
    private static readonly string[] C172Names = ["Pilot", "Co-Pilot", "Passenger Left", "Passenger Right", "Tail 1", "Tail 2"];

    [Fact]
    public void The_real_172_stations_map_to_the_right_roles()
    {
        var map = StationMapper.Map(C172Names);
        Assert.Equal([StationRole.Pilot, StationRole.FrontSeat, StationRole.RearSeat, StationRole.RearSeat, StationRole.BaggageA, StationRole.BaggageB], map.Select(x => x.Role));
    }

    [Fact]
    public void Roles_come_from_names_whatever_the_order()
    {
        var map = StationMapper.Map(["Baggage", "Rear seat", "Pilot", "Front passenger"]);
        Assert.Equal([StationRole.BaggageA, StationRole.RearSeat, StationRole.Pilot, StationRole.FrontSeat], map.Select(x => x.Role));
    }

    [Fact]
    public void Planned_load_lands_on_the_right_sim_stations_and_never_touches_the_pilot()
    {
        var sim = StationMapper.Map(C172Names);
        var load = LoadPlanner.Compute(new LoadRequest
        {
            Aircraft = AircraftProfile.C172, PassengerKg = [100, 70, 60], BaggageKg = 10, FuelGal = 20,
        }).Stations;
        var w = StationMapper.ToSim(sim, load);
        Assert.False(w.ContainsKey(1));
        Assert.Equal(Units.KgToLb(100), w[2], 1);
        Assert.Equal(Units.KgToLb(70), w[3], 1);
        Assert.Equal(Units.KgToLb(60), w[4], 1);
        Assert.Equal(Units.KgToLb(10), w[5], 1);
        Assert.Equal(0, w[6], 1);
    }

    [Fact]
    public void Total_written_weight_equals_total_planned_weight()
    {
        var sim = StationMapper.Map(C172Names);
        var load = LoadPlanner.Compute(new LoadRequest
        {
            Aircraft = AircraftProfile.C172, PassengerKg = [90, 80], BaggageKg = 55, FuelGal = 20,
        }).Stations;
        double planned = load.Where(s => s.Role != StationRole.Pilot).Sum(s => s.Lb);
        Assert.Equal(planned, StationMapper.ToSim(sim, load).Values.Sum(), 1);
    }

    [Fact]
    public void Getting_out_empties_every_loaded_station_except_the_pilot()
    {
        var empty = StationMapper.Empty(StationMapper.Map(C172Names));
        Assert.Equal(5, empty.Count);
        Assert.False(empty.ContainsKey(1));
        Assert.All(empty.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Fuel_is_written_as_equal_levels_across_tanks()
    {
        var levels = FuelWrite.Levels(new Dictionary<string, double> { ["LEFT MAIN"] = 26.5, ["RIGHT MAIN"] = 26.5 }, 26.5);
        Assert.Equal(0.5, levels["LEFT MAIN"], 3);
        Assert.Equal(0.5, levels["RIGHT MAIN"], 3);
    }
}
