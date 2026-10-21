using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class TextureSourceEditor : UserControl
{
    public TextureSourceEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextFlyout?.ShowAt(button);
        }
    }

    private void ChangeTextureSourceType(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextureSourceEditorViewModel { IsDisposed: false } vm) return;
        if (sender is not FARadioMenuFlyoutItem { Tag: string tag }) return;

        switch (tag)
        {
            case "Image":
                vm.ChangeToImageTextureSource();
                break;
            case "Drawable":
                vm.ChangeToDrawableTextureSource();
                break;
            case "Null":
                vm.ChangeToNull();
                break;
        }
    }
}
