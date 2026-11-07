using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlightCompanion.Game.Talk;
using FlightCompanion.Game.World;
using FlightCompanion.Services;

namespace FlightCompanion.Views;

/// <summary>
/// Checks each outside connection on its own and prints plain results, without the sim, the mic or the GPU.
/// Run with --selftest. It spends a few ElevenLabs characters on one short sentence, and one token of Claude.
/// </summary>
internal static class SelfTest
{
    /// <summary>Real Claude: being harassed in a seat you cannot leave, then getting out on the ground.</summary>
    public static async Task HarassedInTheAirAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var brain = new Brain(new KeyStore(), AppSettings.Load());
        var ct = CancellationToken.None;
        var report = new List<string>();
        const string Line = "Du får gratis tur hvis jeg får knulle deg.";
        var hege = new Game.People.Person { Name = "Hege Lund", Language = "no", SpeaksNorwegian = true, Home = "Finnmark" };
        var m = new Game.Missions.Mission { CallerId = hege.Id, Title = "Tur", Premise = "You need to get to Lakselv.", Phase = Game.Missions.MissionPhase.Airborne, Plan = new Game.Missions.MissionPlan { PickupId = "ENMP", DropoffId = "ENNA" } };
        var career = new Game.Career.CareerState();
        const string Where = "[You are flying from Mollisjok to Lakselv, 3000 feet above the ground.]";
        async Task Run(string title, string system, params string[] said)
        {
            var hist = new List<Turn>();
            foreach (var s in said)
            {
                hist.Add(new Turn(true, s));
                var a = new System.Text.StringBuilder();
                await foreach (var d in brain.SpeakAsync(system, hist, ct)) a.Append(d);
                hist.Add(new Turn(false, a.ToString()));
                report.Add($"{title} \"{s[..Math.Min(s.Length, 30)]}\": {a}");
            }
        }
        string before = Game.Talk.Prompts.Speaker(hege, m, career, Game.Talk.TalkMode.Intercom, null, "no", Game.Talk.Dialect.Unknown);
        for (int i = 0; i < 3; i++) await Run("FIRST", before, $"{Where}\n{Line}");
        var note = await brain.ListenAsync(Game.Talk.Prompts.ScribeUser(DateTimeOffset.Now, m.Plan, ["Hege Lund: Fin utsikt.", $"Matt: {Line}", "Hege Lund: Hva faen? Nei!"], 0), ct);
        report.Add($"NOTE severity {note?.OffenceSeverity}, quote {note?.OffenceQuote}");
        async Task Clean(string title, params string[] lines)
        {
            var n = await brain.ListenAsync(Game.Talk.Prompts.ScribeUser(DateTimeOffset.Now, m.Plan, lines, 0), ct);
            report.Add($"NOTE {title}: severity {n?.OffenceSeverity}, quote {n?.OffenceQuote}");
        }
        foreach (var run in new[] { 1, 2 })
        {
            await Clean("says who they are (want none)", "Ingrid Somby: Eg veit ikkje kem du e.", "Matt: Nei, det er sjefen.", "Ingrid Somby: Sjefen, ja?");
            await Clean("wants the details (want none)", "Ingrid Somby: Han sa noe stygt.", "Matt: Vi må vite nøyaktig hva han sa.", "Ingrid Somby: Eg har sagt det.");
            await Clean("the real thing (want 2)", "Ingrid Somby: Kan du komme?", "Matt: Ja, hvis jeg får lov til å knulle deg.", "Ingrid Somby: Nei. Eg legg på no.");
            await Clean("a real insult (want 1)", "Ingrid Somby: Kan du komme?", "Matt: Hold kjeft, din dumme ku.", "Ingrid Somby: Ka sa du?");
        }
        var present = new List<Game.People.Person> { hege };
        Game.Career.Offences.Apply(career, m, present, "Piloten kom med et seksuelt tilbud.", 2, DateTimeOffset.Now);
        m.OffenceQuotes.Add(Line);
        report.Add($"PHASE after offence: {m.Phase}, texts {career.Inbox.Count}");
        string after = Game.Talk.Prompts.Speaker(hege, m, career, Game.Talk.TalkMode.Intercom, null, "no", Game.Talk.Dialect.Unknown);
        for (int i = 0; i < 2; i++) await Run("AFTER", after, $"{Where}\nSer du utsikten der borte?", "Det var bare tull, slapp av.", "Beklager det der.", "Tilbudet står fortsatt, gratis tur for sex.");
        m.Phase = Game.Missions.MissionPhase.Landed;
        string down = Game.Talk.Prompts.Speaker(hege, m, career, Game.Talk.TalkMode.Debrief, null, "no", Game.Talk.Dialect.Unknown);
        for (int i = 0; i < 3; i++) await Run("LANDED", down, "[what just happened: The wheels are on the ground. You are down, you are safe, and you do not have to hold back any more. Everything you have been holding in since the pilot said that comes out now.]", "Da blir det to tusen kroner.", "Når skal jeg hente deg igjen?");
        File.WriteAllLines(Path.Combine(outDir, "air.txt"), report);
    }

    /// <summary>Real Claude on the things that only show up with the real model: the note taker's fields, and how people talk on board.</summary>
    public static async Task TalkAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var settings = AppSettings.Load();
        var brain = new Brain(new KeyStore(), settings);
        var ct = CancellationToken.None;
        var now = DateTimeOffset.Now;
        var report = new List<string> { $"Now {Game.Missions.Clock.ToLocal(now):HH:mm}" };
        var plan = new Game.Missions.MissionPlan { PickupId = "ENMP", DropoffId = "ENNA" };

        async Task Note(string title, params string[] lines)
        {
            try
            {
                var p = await brain.ListenAsync(Game.Talk.Prompts.ScribeUser(now, plan, lines, 22), ct);
                report.Add($"NOTE {title}: time {p?.PickupTimeLocal}, price {p?.PriceNok}, each {p?.PricePerPersonNok}, agreed {p?.BothAgreed}, tour {p?.TourPlace}, offence {p?.OffenceSeverity} {p?.OffenceNote}, quote {p?.OffenceQuote}, real {Game.Career.Offences.SaidByPilot(p?.OffenceQuote, lines.Where(l => l.StartsWith("Matt:")))}, sorry {p?.Apology}");
            }
            catch (Exception ex) { report.Add($"NOTE {title} FAILED: {ex.GetType().Name} {ex.Message}"); }
        }
        await Note("now", "Ingrid: Jeg må til Lakselv.", "Matt: Jeg kommer nå med en gang, to tusen kroner.", "Ingrid: Ok, det er greit. Jeg står klar.");
        await Note("in an hour", "Ingrid: Når kan du komme?", "Matt: Om en time, 3000 per person.", "Ingrid: Avtale.");
        await Note("proposition", "Kamilla: Hva koster turen?", "Matt: Null kroner hvis jeg får knulle deg.", "Kamilla: Hva faen? Nei!");
        await Note("swearing is fine", "Per: Går det bra?", "Matt: Ja for faen, slapp av din jævla tulling, jeg kommer.");
        await Note("tour", "Per: Kan du fly over Sautso-canyonet på veien?", "Matt: Ja, det kan vi.");

        var mathis = new Game.People.Person { Name = "Mathis Guttorm", Language = "no", SpeaksNorwegian = true, Home = "Sápmi" };
        var mission = new Game.Missions.Mission { CallerId = mathis.Id, Title = "Tur", Premise = "You need a lift.", Phase = Game.Missions.MissionPhase.Airborne, Plan = new Game.Missions.MissionPlan { PickupId = "ENMP", DropoffId = "ENNA" } };
        string system = Game.Talk.Prompts.Speaker(mathis, mission, new Game.Career.CareerState(), Game.Talk.TalkMode.Intercom, null, "no", Game.Talk.Dialect.Unknown);

        async Task Say(string title, string where, string pilot)
        {
            for (int i = 0; i < 3; i++)
            {
                var sb = new System.Text.StringBuilder();
                await foreach (var d in brain.SpeakAsync(system, [new Turn(true, $"[It is {Game.Missions.Clock.ToLocal(now):dddd HH:mm}. {where}]\n{pilot}")], ct)) sb.Append(d);
                report.Add($"SAY {title}: {sb}");
            }
        }
        string cruise = Game.Talk.Prompts.WhereNow("Mollisjok", "Lakselv", "Lakselv", 22, 12, "Ravnastua", 9);
        string final = Game.Talk.Prompts.WhereNow("Mollisjok", "Lakselv", "Lakselv", 0.3, 1, "Lakselv", 1, Game.Talk.Prompts.LandingNote("Lakselv", 0.3, 90, -450, 70, false));
        await Say("cruise, chat", cruise, "Fin dag i dag.");
        await Say("cruise, nonsense from speech recognition", cruise, "Og så, og så, rislinga.");
        await Say("cruise, destination", cruise, "Vi skal til Lakselv.");
        await Say("over the threshold", final, "Da lander vi.");
        var hege = new Game.People.Person { Name = "Hege Lund", Language = "no", SpeaksNorwegian = true, Home = "Finnmark", Trust = -50 };
        hege.Note(now.AddDays(-1), "Piloten kom med en seksuell kommentar.", -45, true);
        var career = new Game.Career.CareerState();
        career.Talks.Add(new Game.Career.TalkRecord { When = now.AddDays(-1), Mode = "Phone", PersonIds = [hege.Id], Offence = 2, HungUp = true, Lines = ["Captain: Du får gratis tur hvis jeg får knulle deg.", "Hege Lund: Hva faen? Nei. Dette forteller jeg folk."] });
        var job = new Game.Missions.Mission { CallerId = hege.Id, Title = "Tur", Premise = "You need to get to Lakselv. You want to go right now.", Phase = Game.Missions.MissionPhase.Negotiating, Plan = new Game.Missions.MissionPlan { PickupId = "ENMP", DropoffId = "ENNA" } };
        string wronged = Game.Talk.Prompts.Speaker(hege, job, career, Game.Talk.TalkMode.Phone, null, "no", Game.Talk.Dialect.Unknown);
        await Note("boss act", "Hege Lund: Jeg husker hva du sa.", "Matt: Dette er sjefen hans. Hva sa kollegaen min til deg? Jeg er lei for det som skjedde.", "Hege Lund: Han ville knulle meg for en gratis tur.");
        await Note("same route after sorry", "Matt: Beklager det der.", "Hege Lund: Ok.", "Matt: Du får gratis tur hvis jeg får knulle deg.");
        foreach (var said in new[] { "Hei, dette er ikke samme person, jeg er sjefen hans. Hva sa han egentlig til deg?", "Jeg er så lei for det som skjedde, det var helt feil. Kan jeg gjøre det godt igjen?" })
            for (int i = 0; i < 3; i++)
            {
                var reply = new System.Text.StringBuilder();
                await foreach (var d in brain.SpeakAsync(wronged, [new Turn(true, "[The pilot just picked up the phone. Say hello.]"), new Turn(false, "Det er Hege Lund. Jeg husker hva du sa sist, og jeg ringer bare fordi jeg må."), new Turn(true, said)], ct)) reply.Append(d);
                report.Add($"SLACK \"{said[..20]}\": {reply}");
            }
        string pilotRang = Game.Talk.Prompts.Speaker(hege, null, career, Game.Talk.TalkMode.Phone, "The pilot is calling you, and you do not know why. Answer and find out.", "no", Game.Talk.Dialect.Unknown);
        for (int i = 0; i < 4; i++)
        {
            var hist = new List<Turn> { new(true, "[You just answered your phone. The pilot is calling. Answer with your name.]") };
            var transcript = new List<string>();
            foreach (var said in new[] { "Hei, jeg ringer fra Vidda Air Charter. Jeg er sjefen, og jeg vil si unnskyld for kollegaen min.", "Han gikk over streken, og det skal ikke skje igjen. Kan jeg få forklare?" })
            {
                var answer = new System.Text.StringBuilder();
                if (transcript.Count == 0)
                {
                    await foreach (var d in brain.SpeakAsync(pilotRang, hist, ct)) answer.Append(d);
                    hist.Add(new Turn(false, answer.ToString().Replace("<<hangup>>", "").Trim()));
                    report.Add($"RANG {i} answers: {answer}");
                    answer.Clear();
                    transcript.Add("x");
                }
                hist.Add(new Turn(true, said));
                await foreach (var d in brain.SpeakAsync(pilotRang, hist, ct)) answer.Append(d);
                hist.Add(new Turn(false, answer.ToString().Replace("<<hangup>>", "").Trim()));
                report.Add($"RANG {i} after \"{said[..16]}\": {answer}");
            }
        }
        for (int i = 0; i < 3; i++)
        {
            var first = new System.Text.StringBuilder();
            await foreach (var d in brain.SpeakAsync(wronged, [new Turn(true, "[The pilot just picked up the phone. Say hello.]")], ct)) first.Append(d);
            var second = new System.Text.StringBuilder();
            await foreach (var d in brain.SpeakAsync(wronged, [new Turn(true, "[The pilot just picked up the phone. Say hello.]"), new Turn(false, first.ToString()), new Turn(true, "Ja, det kan vi gjøre. Når da?")], ct)) second.Append(d);
            report.Add($"WRONGED opens: {first}");
            report.Add($"WRONGED after yes, when: {second}");
        }
        File.WriteAllLines(Path.Combine(outDir, "talk.txt"), report);
    }

    /// <summary>Renders the panic lines in one voice, saves them to listen to, and reads them back with Whisper to see if the direction in brackets was spoken.</summary>
    public static async Task ScreamsAsync(string outDir)
    {
        var settings = AppSettings.Load();
        var api = new ElevenLabs(new KeyStore(), settings);
        var ct = CancellationToken.None;
        Directory.CreateDirectory(outDir);
        var voices = await api.ListVoicesAsync(ct);
        var voice = voices.First(v => v.Language is "no" or "nb" || v.Accent.Contains("norw", StringComparison.OrdinalIgnoreCase));
        using var factory = Whisper.net.WhisperFactory.FromPath(Path.Combine(AppPaths.Models, "ggml-small.bin"));
        using var whisper = factory.CreateBuilder().WithLanguage("no").Build();
        int n = 0;
        var report = new List<string> { $"Model {api.CurrentModel}" };
        foreach (var line in Game.Talk.ReactionLines.PanicNo.Take(3))
        {
            byte[] pcm = await api.RenderPanicAsync(line, voice.Id, "no", ct);
            var samples = new float[pcm.Length / 2];
            float peak = 0;
            double sum = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
                peak = Math.Max(peak, Math.Abs(samples[i]));
                sum += samples[i] * samples[i];
            }
            string wav = Path.Combine(outDir, $"scream{++n}.wav");
            using (var w = new NAudio.Wave.WaveFileWriter(wav, new NAudio.Wave.WaveFormat(24000, 16, 1))) w.Write(pcm, 0, pcm.Length);
            var at16 = new float[(int)(samples.Length * 16000L / 24000)];
            for (int i = 0; i < at16.Length; i++) at16[i] = samples[(int)(i * 24000L / 16000)];
            var heard = new System.Text.StringBuilder();
            await foreach (var seg in whisper.ProcessAsync(at16, ct)) heard.Append(seg.Text);
            report.Add($"{line} | {samples.Length / 24000.0:0.0} s, peak {peak:0.00}, rms {Math.Sqrt(sum / samples.Length):0.00} | heard: {heard}");
        }
        File.WriteAllLines(Path.Combine(outDir, "screams.txt"), report);
    }

    public static async Task RunAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        _results = Path.Combine(outDir, "results.txt");
        File.Delete(_results);
        var ct = CancellationToken.None;
        var keys = new KeyStore();
        var settings = AppSettings.Load();

        await Step("Voice detection (Silero on CPU)", () =>
        {
            using var vad = new SileroVadRunner();
            var silence = new float[SpeechGate.FrameSamples];
            double quiet = 0;
            for (int i = 0; i < 10; i++) quiet = Math.Max(quiet, vad.Probability(silence));
            return Task.FromResult($"loads and runs, silence scores {quiet:0.000}");
        });

        await Step("ElevenLabs key and voices", async () =>
        {
            if (string.IsNullOrEmpty(keys.ElevenLabs)) return "NO KEY";
            var api = new ElevenLabs(keys, settings);
            var list = await api.ListVoicesAsync(ct);
            return $"{list.Count} voices, for example {string.Join(", ", list.Take(4).Select(v => $"{v.Name} ({v.Gender}, {v.Accent})"))}";
        });

        await Step("ElevenLabs plan", async () =>
        {
            if (string.IsNullOrEmpty(keys.ElevenLabs)) return "NO KEY";
            using var doc = System.Text.Json.JsonDocument.Parse(await new ElevenLabs(keys, settings).GetRawAsync("v1/user/subscription", ct));
            var r = doc.RootElement;
            return $"{r.GetProperty("tier").GetString()}, used {r.GetProperty("character_count").GetInt64()} of {r.GetProperty("character_limit").GetInt64()} credits, voice slots {r.GetProperty("voice_slots_used").GetInt32()} of {r.GetProperty("voice_limit").GetInt32()}";
        });

        await Step("Norwegian voices in the ElevenLabs library", async () =>
        {
            if (string.IsNullOrEmpty(keys.ElevenLabs)) return "NO KEY";
            string raw = await new ElevenLabs(keys, settings).GetRawAsync("v1/shared-voices?language=no&page_size=100&sort=cloned_by_count", ct);
            File.WriteAllText(Path.Combine(outDir, "shared-voices.json"), raw);
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var v = doc.RootElement.GetProperty("voices");
            return $"{v.GetArrayLength()} found, for example " + string.Join("; ", v.EnumerateArray().Take(8).Select(x => $"{x.GetProperty("name").GetString()} ({x.GetProperty("gender").GetString()}, {x.GetProperty("accent").GetString()})"));
        });

        await Step("ElevenLabs Norwegian speech, streaming", async () =>
        {
            if (string.IsNullOrEmpty(keys.ElevenLabs)) return "NO KEY";
            var api = new ElevenLabs(keys, settings);
            var voices = await api.ListVoicesAsync(ct);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long first = -1, total = 0;
            string path = Path.Combine(outDir, "norsk.pcm");
            await using (var f = File.Create(path))
            {
                await api.StreamAsync("Hei, det er Klaus. Vi er tre personer, og vi har seksti kilo bagasje.", voices[0].Id, "no", null, (b, c) =>
                {
                    if (first < 0) first = sw.ElapsedMilliseconds;
                    total += c;
                    f.Write(b, 0, c);
                }, ct);
            }
            bool v4 = File.ReadAllLines(Path.Combine(AppPaths.Logs, $"FlightCompanion_{DateTime.Now:yyyy-MM-dd}.log")).TakeLast(40).Any(l => l.Contains("is not available on this account"));
            return $"{(v4 ? "fell back to Flash" : "V4 Turbo")}: first audio after {first} ms, {total / 48000.0:0.0} s of speech, saved {path}";
        });

        await Step("Voice detection on real speech", () =>
        {
            string pcm = Path.Combine(outDir, "norsk.pcm");
            if (!File.Exists(pcm)) return Task.FromResult("no speech sample");
            // 24 kHz from the voice service down to 16 kHz, with silence either side, through the same gate the mic uses.
            var bytes = File.ReadAllBytes(pcm);
            var audio = new List<float>();
            audio.AddRange(new float[16000]);
            for (double i = 0; i + 1 < bytes.Length / 2; i += 1.5)
                audio.Add(BitConverter.ToInt16(bytes, (int)i * 2) / 32768f);
            audio.AddRange(new float[16000]);
            using var vad = new SileroVadRunner();
            var gate = new SpeechGate();
            var seen = new List<string>();
            double peak = 0;
            int utteranceMs = 0;
            for (int f = 0; (f + 1) * SpeechGate.FrameSamples <= audio.Count; f++)
            {
                var frame = audio.GetRange(f * SpeechGate.FrameSamples, SpeechGate.FrameSamples).ToArray();
                double prob = vad.Probability(frame);
                peak = Math.Max(peak, prob);
                var ev = gate.Process(frame, prob);
                if (ev != GateEvent.None) seen.Add($"{ev} at {f * 32} ms");
                if (ev == GateEvent.SpeechEnded) utteranceMs += gate.CompletedUtterance!.Length / 16;
            }
            return Task.FromResult($"peak {peak:0.00}, kept {utteranceMs} ms: {string.Join(", ", seen)}");
        });

        await Step("Claude key", async () =>
        {
            if (string.IsNullOrEmpty(keys.Anthropic)) return "NO KEY YET";
            var brain = new Brain(keys, settings);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string reply = await brain.OneLineAsync("Answer in one Norwegian word.", "Hei", ct);
            return $"replied \"{reply}\" in {sw.ElapsedMilliseconds} ms";
        });

        await Step("Interrupting someone mid sentence", async () =>
        {
            using var audio = new AudioOut();
            var fake = new FakeSpeaker();
            var clips = new ClipCache(new ElevenLabs(keys, settings), settings);
            var who = new Game.People.Person { Name = "Test", Language = "no", SpeaksNorwegian = true };
            using var talk = new Conversation(fake, new SilentVoice(), clips, new VoiceCaster(new ElevenLabs(keys, settings)), audio, [new CastMember(who, "none", Dialect.Northern, "Test")], VoiceFx.Phone, "sys", () => "situation", "no");
            talk.Open("Say hello");
            await Task.Delay(1200, ct);              // first sentence has played, the second is playing
            talk.PilotStartedTalking();
            talk.OnPilotSpoke("Vent litt", "no");
            await Task.Delay(1000, ct);              // second reply starts
            talk.OnPilotSpoke("Hva sa du", "no");
            await Task.Delay(5000, ct);              // third reply plays to its end
            var h = talk.History;
            bool neverEndedOnThem = fake.LastTurnFromPilot.All(x => x);
            string shape = string.Join(" / ", h.Select(t => (t.FromPilot ? "P:" : "T:") + t.Text));
            if (!neverEndedOnThem) throw new InvalidOperationException("A request ended on their own words, the API would refuse it: " + shape);
            if (fake.Requests < 3) throw new InvalidOperationException($"Only {fake.Requests} requests: {shape}");
            return $"{fake.Requests} requests, each ending on the pilot. History: {shape}";
        });

        await Step("Scripted call with the real models", async () =>
        {
            if (string.IsNullOrEmpty(keys.Anthropic)) return "skipped, no Claude key yet";
            return await ScriptedCall(keys, settings, outDir, ct);
        });

        await Step("Dialects with the real model", async () =>
        {
            if (string.IsNullOrEmpty(keys.Anthropic)) return "skipped, no Claude key yet";
            var brain = new Brain(keys, settings);
            var career = new Game.Career.CareerState { PilotName = "Matt" };
            var lines = new List<string>();
            foreach (var d in new[] { Dialect.Northern, Dialect.Bergen, Dialect.Trondersk, Dialect.Oslo })
            {
                var who = new Game.People.Person { Name = "Kari Nilsen", Home = "Finnmark", Language = "no", SpeaksNorwegian = true, Job = "hunter", Personality = "Practical.", Temper = 0.5 };
                var text = new System.Text.StringBuilder();
                await foreach (var x in brain.SpeakAsync(Prompts.Speaker(who, null, career, TalkMode.Phone, "You want a flight from Lakselv to Mollisjok tomorrow.", "no", d), [new Turn(true, "[The pilot just picked up.]\nHallo, Matt her. Hvor mange er dere, hvor mye bagasje har dere, og hva tenkte du å betale?")], ct))
                    text.Append(x);
                lines.Add($"{d}: {SpeechText.Clean(text.ToString())}");
            }
            File.WriteAllLines(Path.Combine(outDir, "dialects.txt"), lines);
            return string.Join(" || ", lines);
        });

        await Step("Several people with the real model", async () =>
        {
            if (string.IsNullOrEmpty(keys.Anthropic)) return "skipped, no Claude key yet";
            var brain = new Brain(keys, settings);
            var career = new Game.Career.CareerState { PilotName = "Matt" };
            var lead = new Game.People.Person { Name = "Mathis Guttorm", Home = "Sápmi", Language = "no", SpeaksNorwegian = true, Job = "reindeer herder", Personality = "Calm, few words.", Temper = 0.2 };
            var oslo = new Game.People.Person { Name = "Camilla Berg", Home = "Oslo", Language = "no", SpeaksNorwegian = true, Job = "photographer", Personality = "Talks a lot, a bit scared of small planes.", Temper = 0.2, Fear = 0.8 };
            var berg = new Game.People.Person { Name = "Eirik Lie", Home = "Bergen", Language = "no", SpeaksNorwegian = true, Job = "engineer", Personality = "Grumpy, thinks this is overpriced.", Temper = 0.9 };
            var sys = Prompts.Speaker(lead, null, career, TalkMode.InPerson, null, "no", Dialect.Northern, [("Camilla", oslo, Dialect.Oslo), ("Eirik", berg, Dialect.Bergen)], "Mathis");
            var labels = new[] { "Mathis", "Camilla", "Eirik" };
            var log = new List<string>();
            var history = new List<Turn>();
            foreach (var line in new[] { "[The pilot has just walked over to you. Greet them.]", "Hei. Hvor mye veier bagasjen deres, Eirik?", "Camilla, er du flyredd eller?", "Hvem av dere vil sitte foran?", "[The pilot is busy and not talking to you right now. You are standing waiting with your bags. Talk a little among yourselves, one or two short lines]" })
            {
                history.Add(new Turn(true, line));
                log.Add("MATT> " + line);
                var split = new SpeakerSplitter(labels);
                var sb = new System.Text.StringBuilder();
                var parsed = new List<string>();
                await foreach (var d in brain.SpeakAsync(sys, history, ct))
                {
                    sb.Append(d);
                    foreach (var (who, s) in split.Push(d)) parsed.Add($"  {who}: {SpeechText.Clean(s)}");
                }
                foreach (var (who, s) in split.Flush()) parsed.Add($"  {who}: {SpeechText.Clean(s)}");
                log.AddRange(parsed);
                history.Add(new Turn(false, sb.ToString().Trim()));
            }
            File.WriteAllLines(Path.Combine(outDir, "group.txt"), log);
            return "written to group.txt";
        });

        await Step("Real map around Mollisjok", async () =>
        {
            var map = new MapKnowledge();
            int n = await map.FetchAsync(69.5291, 24.3923, ct);
            var seen = SightWatcher.Visible(69.60, 24.40, 180, WorldData.Sights.Concat(map.Known), 6);
            return $"{n} named places. Flying south just north of the lodge: " + string.Join("; ", seen.Select(s => s.Describe()));
        });

        await Step("METAR feed", async () =>
        {
            var w = new WeatherFeed();
            await w.RefreshMetarsAsync(ct);
            var banak = w.Metar("ENNA");
            return banak is null ? "NO DATA" : $"ENNA: {banak.Summary()} ({banak.Category})";
        });

        await Step("Forecast at Mollisjok", async () =>
        {
            var w = new WeatherFeed();
            var f = await w.ForecastAsync(WorldData.ById("ENMP")!, DateTimeOffset.Now.AddHours(20), ct);
            return f is null ? "NO DATA" : $"in 20 hours: {f.Summary()} ({f.Category})";
        });

        await Step("SimConnect library", () =>
        {
            try
            {
                using var sim = new Microsoft.FlightSimulator.SimConnect.SimConnect("FlightCompanion selftest", IntPtr.Zero, 0, null, 0);
                return Task.FromResult("loads, and a sim is running");
            }
            catch (System.Runtime.InteropServices.COMException) { return Task.FromResult("loads, no sim running right now"); }
        });

        await Step("Community folder", () => Task.FromResult(SimLink.FindCommunityFolder() ?? "not found, the game still works but places no passenger figures"));

        foreach (var model in SpeechModels.All)
        {
            string file = Path.Combine(AppPaths.Models, model.FileName);
            await Step("Speech model " + model.Id, async () =>
            {
                if (!File.Exists(file)) return "not downloaded";
                string pcm = Path.Combine(outDir, "norsk.pcm");
                if (!File.Exists(pcm)) return "no speech sample";
                // On the CPU at idle priority: this compares what each one hears. Speed on the GPU is measured in real calls.
                if (Environment.GetEnvironmentVariable("FC_WHISPER_GPU") is null) Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = [Whisper.net.LibraryLoader.RuntimeLibrary.Cpu];
                var bytes = File.ReadAllBytes(pcm);
                var audio = new List<float>();
                for (double i = 0; i + 1 < bytes.Length / 2; i += 1.5) audio.Add(BitConverter.ToInt16(bytes, (int)i * 2) / 32768f);
                using var factory = Whisper.net.WhisperFactory.FromPath(file);
                using var proc = factory.CreateBuilder().WithLanguage("no").WithThreads(2).Build();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var text = new System.Text.StringBuilder();
                await foreach (var seg in proc.ProcessAsync(audio.ToArray(), ct)) text.Append(seg.Text);
                return $"{audio.Count / 16} ms of audio in {sw.ElapsedMilliseconds} ms : {text.ToString().Trim()}";
            });
        }
    }

    private sealed class FakeSpeaker : ISpeakerModel
    {
        public int Requests;
        public readonly List<bool> LastTurnFromPilot = [];

        public async IAsyncEnumerable<string> SpeakAsync(string system, IReadOnlyList<Turn> history, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            Requests++;
            LastTurnFromPilot.Add(history[^1].FromPilot);
            foreach (var word in "Første setning her. Andre setning kommer nå og den er ganske lang. Tredje setning til slutt.".Split(' '))
            {
                await Task.Delay(30, ct);
                yield return word + " ";
            }
        }
    }

    /// <summary>Half a second of silence for each sentence: the timing of speech without making a sound.</summary>
    private sealed class SilentVoice : IVoiceStream
    {
        public async Task StreamAsync(string text, string voiceId, string language, string? previousText, Action<byte[], int> onPcm, CancellationToken ct)
        {
            var chunk = new byte[VoiceChannel.SampleRate / 10 * 2];
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(20, ct);
                onPcm(chunk, chunk.Length);
            }
        }
    }

    /// <summary>A whole negotiation in Norwegian, text only: real Claude replies, real note taking, measured latency, no voice credits.</summary>
    private static async Task<string> ScriptedCall(KeyStore keys, AppSettings settings, string outDir, CancellationToken ct)
    {
        var brain = new Brain(keys, settings);
        var career = new Game.Career.CareerState { PilotName = "Matt" };
        var rng = new Random(7);
        var tpl = Game.Missions.MissionCatalog.ById("fishing_camp")!;
        var person = Game.People.CastFactory.Create(rng, "German", "salmon angler");
        person.Honesty = 0;
        career.People.Add(person);
        var m = Game.Missions.MissionFactory.Create(tpl, new Game.Career.CareerView(career), rng, DateTimeOffset.Now, person);
        career.Missions.Add(m);
        string system = Prompts.Speaker(person, m, career, TalkMode.Phone, null, "no");
        var history = new List<Turn>();
        var log = new System.Text.StringBuilder();
        var times = new List<long>();

        async Task Turn(string user)
        {
            history.Add(new Turn(true, user));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long first = -1;
            var text = new System.Text.StringBuilder();
            await foreach (var d in brain.SpeakAsync(system, history, ct))
            {
                if (first < 0) first = sw.ElapsedMilliseconds;
                text.Append(d);
            }
            times.Add(first);
            string reply = SpeechText.Clean(text.ToString());
            history.Add(new Turn(false, reply));
            log.AppendLine($"[{first} ms] {person.Name}: {reply}");
        }

        await Turn("[The pilot just picked up the phone. Say hello.]");
        foreach (var line in new[]
        {
            "Hei, Matt her. Hvor mange er dere, og hvor mye bagasje har dere?",
            "Det blir tungt inn til stripa. Kan dere klare dere med tjue kilo mindre?",
            "Greit. Hva med i morgen klokka to? Det blir seks tusen kroner.",
            "Avtale. Vi snakkes, ha det.",
        })
        {
            log.AppendLine("Matt: " + line);
            await Turn(line);
        }

        var transcript = history.Where(t => !t.Text.StartsWith('[')).Select(t => (t.FromPilot ? "Matt: " : person.Name + ": ") + t.Text);
        var patchWatch = System.Diagnostics.Stopwatch.StartNew();
        var patch = await brain.ListenAsync(Prompts.ScribeUser(DateTimeOffset.Now, m.Plan, transcript), ct);
        log.AppendLine($"Notes after {patchWatch.ElapsedMilliseconds} ms: {System.Text.Json.JsonSerializer.Serialize(patch)}");
        log.AppendLine("Hidden truth: " + string.Join(" | ", m.SecretsForCaller));
        File.WriteAllText(Path.Combine(outDir, "scripted-call.txt"), log.ToString());
        times.Sort();
        return $"first words median {times[times.Count / 2]} ms, worst {times[^1]} ms, notes in {patchWatch.ElapsedMilliseconds} ms, transcript in scripted-call.txt";
    }

    private static string _results = "";

    private static async Task Step(string name, Func<Task<string>> run)
    {
        string line;
        try { line = $"{name}: {await run()}"; }
        catch (Exception ex) { line = $"{name}: FAILED {ex.GetType().Name}: {Scrub(ex.Message)}"; }
        Console.WriteLine(line);
        File.AppendAllText(_results, line + Environment.NewLine);
    }

    private static string Scrub(string s) => s.Length > 260 ? s[..260] : s;
}
