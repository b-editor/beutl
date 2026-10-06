using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public sealed partial class CreateNewScene : FAContentDialog
{
    public CreateNewScene()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    // 場所を選択
    private async void PickLocation(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CreateNewSceneViewModel vm
            && await LocationPicker.PickFolderAsync(this) is { } localPath)
        {
            vm.Location.Value = localPath;
        }
    }
}
