using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;

using BeUtl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Views.Dialogs;

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
            var picker = new OpenFolderDialog();

            string? result = await picker.ShowAsync(parent);

            if (result != null)
            {
                vm.Location.Value = result;
            }
        }
    }
}
