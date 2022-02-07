using Avalonia.Controls;
using Avalonia.Interactivity;

using BeUtl.ViewModels.SettingsPages;

namespace BeUtl.Pages.SettingsPages;

public partial class FontSettingsPage : UserControl
{
    public FontSettingsPage()
    {
        InitializeComponent();
    }

    public async void AddClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is Window window && DataContext is FontSettingsPageViewModel vm)
        {
            var dialog = new OpenFolderDialog();
            string? dir = await dialog.ShowAsync(window);

            if (Directory.Exists(dir) && dir != null)
            {
                vm.FontDirectories.Add(dir);
            }
        }
    }
}
