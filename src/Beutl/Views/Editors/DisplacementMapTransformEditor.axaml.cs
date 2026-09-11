using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class DisplacementMapTransformEditor : UserControl
{
    public DisplacementMapTransformEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);
        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(this, (FAMenuFlyout)expandToggle.ContextFlyout!);
    }

    private void Tag_Click(object? sender, RoutedEventArgs e)
    {
        var target = sender as Button ?? expandToggle;

        expandToggle.ContextFlyout?.ShowAt(target);
    }

    private void TransformTypeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not FAMenuFlyoutItem { Tag: string type }) return;
        if (DataContext is not DisplacementMapTransformEditorViewModel { IsDisposed: false } viewModel) return;

        viewModel.ChangeType(type switch
        {
            "Translate" => DispMapTransformType.Translate,
            "Rotation" => DispMapTransformType.Rotation,
            "Scale" => DispMapTransformType.Scale,
            _ => DispMapTransformType.Null
        });
    }
}
