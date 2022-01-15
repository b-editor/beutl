using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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

    // êŠ‚ğ‘I‘ğ
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
