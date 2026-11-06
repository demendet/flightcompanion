using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FlightCompanion.Services;
using FlightCompanion.ViewModels;
using FlightCompanion.Views;

namespace FlightCompanion;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var host = new GameHost();
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(host) };
            desktop.ShutdownRequested += (_, _) => host.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
