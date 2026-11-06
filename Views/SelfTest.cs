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
            var clips = new ClipCache(new ElevenLabs(keys, settings));
            var who = new Game.People.Person { Name = "Test", Language = "no", SpeaksNorwegian = true };
            using var talk = new Conversation(fake, new SilentVoice(), clips, new VoiceCaster(new ElevenLabs(keys, settings)), audio, who, "none", VoiceFx.Phone, "sys", () => "situation", "no");
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
                Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = [Whisper.net.LibraryLoader.RuntimeLibrary.Cpu];
                var bytes = File.ReadAllBytes(pcm);
                var audio = new List<float>();
                for (double i = 0; i + 1 < bytes.Length / 2; i += 1.5) audio.Add(BitConverter.ToInt16(bytes, (int)i * 2) / 32768f);
                using var factory = Whisper.net.WhisperFactory.FromPath(file);
                using var proc = factory.CreateBuilder().WithLanguage("no").WithThreads(2).Build();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var text = new System.Text.StringBuilder();
                await foreach (var seg in proc.ProcessAsync(audio.ToArray(), ct)) text.Append(seg.Text);
                return $"{audio.Count / 16} ms of audio in {sw.ElapsedMilliseconds} ms on 2 CPU threads: {text.ToString().Trim()}";
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
