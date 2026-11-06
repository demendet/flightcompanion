using System;
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
