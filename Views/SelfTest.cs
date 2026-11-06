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
            var api = new ElevenLabs(keys);
            var list = await api.ListVoicesAsync(ct);
            return $"{list.Count} voices, for example {string.Join(", ", list.Take(4).Select(v => $"{v.Name} ({v.Gender}, {v.Accent})"))}";
        });

        await Step("ElevenLabs Norwegian speech, streaming", async () =>
        {
            if (string.IsNullOrEmpty(keys.ElevenLabs)) return "NO KEY";
            var api = new ElevenLabs(keys);
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
            return $"first audio after {first} ms, {total / 48000.0:0.0} s of speech, saved {path}";
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

        await Step("Whisper model file", () =>
        {
            string p = Path.Combine(AppPaths.Models, settings.WhisperModelFile);
            return Task.FromResult(File.Exists(p) ? $"present, {new FileInfo(p).Length / 1_000_000} MB (not loaded, it would use the GPU)" : "MISSING, it downloads on first start");
        });
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
