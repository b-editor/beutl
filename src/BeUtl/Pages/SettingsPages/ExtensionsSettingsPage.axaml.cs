using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BeUtl.Pages.SettingsPages;

public partial class ExtensionsSettingsPage : UserControl
{
    public ExtensionsSettingsPage()
    {
        InitializeComponent();
    }

    private async void Add_FileExtension(object? sender, RoutedEventArgs e)
    {
        await Dialog1.ShowAsync();
    }
}
