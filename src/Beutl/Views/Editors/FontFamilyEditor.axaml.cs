using Avalonia.Controls;

using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using Beutl.Views.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public sealed partial class FontFamilyEditor : UserControl
{
    public FontFamilyEditor()
    {
        InitializeComponent();
        button.Click += Button_Click;
    }

    private async void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not FontFamilyEditorViewModel vm) return;

        var dialogViewModel = new PickFontFamilyViewModel
        {
            SelectedItem =
            {
                Value = vm.WrappedProperty.GetValue()
            }
        };
        var dialog = new PickFontFamily
        {
            DataContext = dialogViewModel
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            vm.SetValue(vm.WrappedProperty.GetValue(), dialogViewModel.SelectedItem.Value);
        }
    }
}
