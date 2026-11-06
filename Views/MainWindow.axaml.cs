using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FlightCompanion.Services;
using FlightCompanion.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FlightCompanion.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, System.EventArgs e)
        {
            // Keep the conversation log pinned to the newest message as it grows.
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ConversationHistory.CollectionChanged -= OnConversationChanged;
                vm.ConversationHistory.CollectionChanged += OnConversationChanged;
            }
        }

        private void OnConversationChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var scroller = this.FindControl<ScrollViewer>("ConversationScrollViewer");
                scroller?.ScrollToEnd();
            }, DispatcherPriority.Background);
        }

        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            // Initialize and embed the map
            var mapService = App.ServiceProvider?.GetService<IMapService>();
            if (mapService != null)
            {
                await mapService.InitializeAsync();

                // Find the MapContainer and add the browser
                var mapContainer = this.FindControl<ContentControl>("MapContainer");
                if (mapContainer != null)
                {
                    mapContainer.Content = mapService.MapBrowser;
                }
            }
        }
    }
}
