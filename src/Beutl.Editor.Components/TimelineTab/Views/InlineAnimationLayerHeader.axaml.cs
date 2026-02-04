using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;

using Beutl.Animation;
using Beutl.Controls.Behaviors;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;

namespace Beutl.Editor.Components.TimelineTab.Views;

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
        if (DataContext is InlineAnimationLayerViewModel viewModel
            && viewModel.Property is IAnimatablePropertyAdapter { Animation: KeyFrameAnimation kfAnimation })
        {
            var editorContext = viewModel.Timeline.EditorContext;
            // タイムラインのタブを開く
            var anmTimelineViewModel = new GraphEditorTabViewModel(editorContext);
            anmTimelineViewModel.Element.Value = viewModel.Element.Model;
            anmTimelineViewModel.Select(kfAnimation);
            editorContext.OpenToolTab(anmTimelineViewModel);
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
