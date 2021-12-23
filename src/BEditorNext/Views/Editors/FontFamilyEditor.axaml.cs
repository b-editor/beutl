using Avalonia.Controls;

using BEditorNext.ViewModels.Dialogs;
using BEditorNext.ViewModels.Editors;
using BEditorNext.Views.Dialogs;

using FluentAvalonia.UI.Controls;

namespace BEditorNext.Views.Editors;

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
                Value = vm.Setter.Value
            }
        };
        var dialog = new PickFontFamily
        {
            DataContext = dialogViewModel
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            vm.SetValue(vm.Setter.Value, dialogViewModel.SelectedItem.Value);
        }
    }
}
