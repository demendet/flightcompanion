using System;
using System.Linq;
using Avalonia;

namespace FlightCompanion;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--shots")
        {
            Views.LayoutPreview.Run(args[1]);
            return;
        }
        if (args.Length == 1 && args[0] == "--store-claude-key")
        {
            // Reads the key from the environment, so it never appears in a command line or a log.
            string? key = Environment.GetEnvironmentVariable("FC_CLAUDE_KEY");
            if (!string.IsNullOrWhiteSpace(key)) new Services.KeyStore().Anthropic = key;
            return;
        }
        if (args.Length == 1 && args[0] == "--add-norwegian-voices")
        {
            var keys = new Services.KeyStore();
            var api = new Services.ElevenLabs(keys, new Services.AppSettings());
            var added = api.AddNorwegianVoicesAsync(4, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            var after = api.ListVoicesAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            System.IO.File.WriteAllLines(System.IO.Path.Combine(Services.AppPaths.Local, "voices-added.txt"),
                added.Concat(after.Select(v => $"HAS {v.Name} | {v.Gender} | {v.Accent} | {v.Language} | {v.Category}")));
            return;
        }
        if (args.Length == 2 && args[0] == "--test-talk")
        {
            AttachConsole(-1);
            Views.SelfTest.TalkAsync(args[1]).GetAwaiter().GetResult();
            return;
        }
        if (args.Length == 2 && args[0] == "--test-screams")
        {
            AttachConsole(-1);
            Views.SelfTest.ScreamsAsync(args[1]).GetAwaiter().GetResult();
            return;
        }
        if (args.Length == 2 && args[0] == "--selftest")
        {
            AttachConsole(-1);
            Views.SelfTest.RunAsync(args[1]).GetAwaiter().GetResult();
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
