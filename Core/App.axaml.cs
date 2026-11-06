using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FlightCompanion.Services;
using FlightCompanion.ViewModels;
using FlightCompanion.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace FlightCompanion
{
    public partial class App : Application
    {
        public static IServiceProvider? ServiceProvider { get; private set; }
        public static IConfiguration? Configuration { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            ConfigureServices();
        }

        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            // Load configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            
            Configuration = builder.Build();
            services.AddSingleton(Configuration);

            // Register services (order matters for dependencies)
            services.AddSingleton<IDebugLogService, DebugLogService>();
            services.AddSingleton<ISimConnectService, SimConnectService>();
            services.AddSingleton<ISignalRService, SignalRService>();
            services.AddSingleton<IMapService, MapService>();
            services.AddSingleton<IOpenAIService, OpenAIService>();
            services.AddSingleton<IElevenLabsService, ElevenLabsService>();
            services.AddSingleton<IFlightPlanService, FlightPlanService>();
            services.AddSingleton<IPayloadService, PayloadService>();
            services.AddSingleton<IAudioService, AudioService>();
            services.AddSingleton<ISpeechRecognitionService, WhisperSpeechRecognitionService>(); // offline Whisper (NO+EN), no key
            services.AddSingleton<IPhoneService, PhoneService>();

            // Register ViewModels
            services.AddTransient<MainWindowViewModel>();

            ServiceProvider = services.BuildServiceProvider();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindowViewModel = ServiceProvider?.GetRequiredService<MainWindowViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainWindowViewModel
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}