using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;

using Beutl.Controls.Behaviors;
using Beutl.ViewModels;

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
