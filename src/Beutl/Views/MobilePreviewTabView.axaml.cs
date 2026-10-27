using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Beutl.ViewModels;

namespace Beutl.Views;

public partial class MobilePreviewTabView : UserControl
{
    public MobilePreviewTabView() => InitializeComponent();

    private MobilePreviewTabViewModel? ViewModel => DataContext as MobilePreviewTabViewModel;

    private async void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.ToggleSharingAsync();
    }

    private void OnRenewCodeClick(object? sender, RoutedEventArgs e) => ViewModel?.RenewCode();
    private void OnDisconnectAllClick(object? sender, RoutedEventArgs e) => ViewModel?.DisconnectAll();
    private void OnConnectionInfoClick(object? sender, RoutedEventArgs e) => ViewModel?.ShowConnectionInfo();
    private void OnDetailsClick(object? sender, RoutedEventArgs e) => ViewModel?.ReturnToDetails();
    private void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: MobilePreviewDevice device }) ViewModel?.Disconnect(device.Id);
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && ViewModel?.Server is { } server)
            await clipboard.SetTextAsync(server.Endpoint.AbsoluteUri);
    }
}
