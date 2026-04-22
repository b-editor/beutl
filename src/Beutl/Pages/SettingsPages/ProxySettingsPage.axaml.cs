using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.Pages.SettingsPages;

public sealed partial class ProxySettingsPage : UserControl
{
    public ProxySettingsPage()
    {
        InitializeComponent();
    }

    private void OnDeleteAllClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProxySettingsPageViewModel vm)
        {
            vm.DeleteAllCache();
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProxyCacheEntryViewModel entry }
            && DataContext is ProxySettingsPageViewModel vm)
        {
            vm.DeleteEntry(entry);
        }
    }

    private void OnRegenerateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProxyCacheEntryViewModel entry }
            && DataContext is ProxySettingsPageViewModel vm)
        {
            vm.RegenerateEntry(entry);
        }
    }
}
