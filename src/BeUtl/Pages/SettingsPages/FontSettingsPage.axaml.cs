using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using BeUtl.ViewModels.SettingsPages;

namespace BeUtl.Pages.SettingsPages;

public sealed partial class FontSettingsPage : UserControl
{
    public FontSettingsPage()
    {
        InitializeComponent();
    }

    public async void AddClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is Window window && DataContext is FontSettingsPageViewModel vm)
        {
            var options = new FolderPickerOpenOptions
            {
                AllowMultiple = true
            };
            var result = await window.StorageProvider.OpenFolderPickerAsync(options);

            foreach (var item in result)
            {
                if (item.TryGetUri(out var uri) && uri.IsFile)
                {
                    vm.FontDirectories.Add(uri.LocalPath);
                }
            }
        }
    }
}
