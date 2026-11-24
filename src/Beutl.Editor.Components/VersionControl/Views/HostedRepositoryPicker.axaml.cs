using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.VersionControl.ViewModels;

namespace Beutl.Editor.Components.VersionControl.Views;

internal sealed partial class HostedRepositoryPicker : UserControl
{
    public HostedRepositoryPicker() => InitializeComponent();

    private async void SignIn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is HostedRepositoryPickerViewModel vm)
            await vm.SignInAsync();
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is HostedRepositoryPickerViewModel vm)
            await vm.RefreshAsync();
    }
}
