using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.Pages.SettingsPages;

public sealed partial class BrowserSettingsPage : UserControl
{
    public BrowserSettingsPage() => InitializeComponent();
    private void OnClearHistoryClick(object? sender, RoutedEventArgs e) =>
        (DataContext as BrowserSettingsPageViewModel)?.ClearHistory();
    private async void OnClearCookiesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BrowserSettingsPageViewModel vm) await vm.ClearCookiesAsync();
    }
}
