using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorTabView : UserControl
{
    public GraphEditorTabView()
    {
        InitializeComponent();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (DataContext is GraphEditorTabViewModel viewModel)
        {
            viewModel.Refresh();
        }
    }

    private void ToggleKeyFrameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: GraphEditorTreeItemViewModel item } control && DataContext is GraphEditorTabViewModel model)
        {
            if (!item.CanAnimate.Value && item.CanRemoveAnimation.Value)
                control.ContextFlyout?.ShowAt(control);
            else
                model.ToggleKeyFrame(item);
            e.Handled = true;
        }
    }

    private void RemoveAnimationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: GraphEditorTreeItemViewModel item } && DataContext is GraphEditorTabViewModel model)
        {
            model.RemoveAnimation(item);
            e.Handled = true;
        }
    }
}
