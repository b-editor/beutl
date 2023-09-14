using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using Beutl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public sealed partial class CreateNewScene : ContentDialog
{
    public CreateNewScene()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    // 場所を選択
    private async void PickLocation(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CreateNewSceneViewModel vm && VisualRoot is Window parent)
        {
            var options = new FolderPickerOpenOptions();
            IReadOnlyList<IStorageFolder> result = await parent.StorageProvider.OpenFolderPickerAsync(options);

            if (result.Count > 0 && result[0].TryGetLocalPath() is string localPath)
            {
                vm.Location.Value = localPath;
            }
        }
    }
}
