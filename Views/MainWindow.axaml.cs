using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using FlightCompanion.ViewModels;

namespace FlightCompanion.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private (int Count, int LastLength, string Partial) _lastChat = (-1, 0, "");

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as MainViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        };
    }

    private async void CopyTripCode(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is { HasTripCode: true } vm && Clipboard is { } clipboard) await clipboard.SetTextAsync(vm.TripCodeText);
    }

    /// <summary>New words appear at the bottom, so that is where the chat stays.</summary>
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Pic) || _vm?.Pic is not { } pic) return;
        var now = (pic.Chat.Count, pic.Chat.Count > 0 ? pic.Chat[^1].Text.Length : 0, pic.Partial);
        if (now == _lastChat) return;
        _lastChat = now;
        // After layout, or the newest line is not measured yet and the scroll stops one line short.
        Dispatcher.UIThread.Post(() => ChatScroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }
}
