#pragma warning disable CS0436

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Beutl.Pages.SettingsPages;
public sealed partial class InfomationPage : UserControl
{
    public InfomationPage()
    {
        InitializeComponent();
    }

    private async void CopyVersion_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(GitVersionInformation.InformationalVersion);
        }
    }
}
