using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

using Beutl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public sealed partial class CreateNewScene : ContentDialog, IStyleable
{
    public CreateNewScene()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);

    // 場所を選択
    private async void PickLocation(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CreateNewSceneViewModel vm && VisualRoot is Window parent)
        {
            var options = new FolderPickerOpenOptions();
            IReadOnlyList<IStorageFolder> result = await parent.StorageProvider.OpenFolderPickerAsync(options);

            if (result.Count > 0
                && result[0].Path is { IsFile: true } uri)
            {
                vm.Location.Value = uri.LocalPath;
            }
        }
    }
}
