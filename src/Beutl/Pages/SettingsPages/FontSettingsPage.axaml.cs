using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using Beutl.ViewModels.SettingsPages;

namespace Beutl.Pages.SettingsPages;

public sealed partial class FontSettingsPage : UserControl
{
    public FontSettingsPage()
    {
        InitializeComponent();
    }

    public async void AddClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is { } storage
            && DataContext is FontSettingsPageViewModel vm)
        {
            var options = new FolderPickerOpenOptions
            {
                AllowMultiple = true
            };
            var result = await storage.OpenFolderPickerAsync(options);

            foreach (var item in result)
            {
                if (item.TryGetLocalPath() is string localPath)
                {
                    vm.FontDirectories.Add(localPath);
                }
            }
        }
    }
}
