using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using FlightCompanion.ViewModels;

namespace FlightCompanion.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private int _lastChatCount = -1;
    private string _lastPartial = "";

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

    /// <summary>New words appear at the bottom, so that is where the chat stays.</summary>
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Pic) || _vm?.Pic is not { } pic) return;
        if (pic.Chat.Count == _lastChatCount && pic.Partial == _lastPartial) return;
        _lastChatCount = pic.Chat.Count;
        _lastPartial = pic.Partial;
        // After layout, or the newest line is not measured yet and the scroll stops one line short.
        Dispatcher.UIThread.Post(() => ChatScroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }
}
