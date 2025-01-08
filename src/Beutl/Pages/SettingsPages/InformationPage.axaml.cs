#pragma warning disable CS0436

using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.Pages.SettingsPages;

public sealed partial class InformationPage : UserControl
{
    public InformationPage()
    {
        InitializeComponent();
    }

    private async void CopyVersion_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard
            && DataContext is InformationPageViewModel vm)
        {
            await clipboard.SetTextAsync(vm.BuildMetadata);
        }
    }
}
