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
