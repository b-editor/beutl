using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Xaml.Interactivity;

using Beutl.Animation;
using Beutl.Controls.Behaviors;
using Beutl.Framework;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;

namespace Beutl.Views;

public partial class InlineAnimationLayerHeader : UserControl
{
    public InlineAnimationLayerHeader()
    {
        InitializeComponent();
        Interaction.GetBehaviors(this).Add(new _DragBehavior()
        {
            DragControl = border,
            Orientation = Orientation.Vertical
        });
    }

    private void OpenTab_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<EditView>()?.DataContext is EditViewModel editViewModel
            && DataContext is InlineAnimationLayerViewModel viewModel
            && viewModel.Property is IAbstractAnimatableProperty { Animation: IKeyFrameAnimation kfAnimation })
        {
            // タイムラインのタブを開く
            var anmTimelineViewModel = new GraphEditorTabViewModel();
            anmTimelineViewModel.SelectedAnimation.Value = new GraphEditorViewModel(editViewModel, kfAnimation, viewModel.Layer.Model);
            editViewModel.OpenToolTab(anmTimelineViewModel);
        }
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (AssociatedObject?.DataContext is InlineAnimationLayerViewModel viewModel
                && viewModel.LayerHeader.Value is { } layerHeader)
            {
                layerHeader.Inlines.Move(oldIndex, newIndex);
            }
        }
    }
}
