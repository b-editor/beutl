using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

using Beutl.Controls.Behaviors;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.Pages.SettingsPages;

// Attached to each list box item, which is both the drag control and the dragged container.
public sealed class DecoderPriorityListBoxItemBehavior : GenericDragBehavior
{
    public DecoderPriorityListBoxItemBehavior()
    {
        Orientation = Orientation.Vertical;
    }

    protected override DragEventHandling HandledEvents => DragEventHandling.Never;

    protected override bool CancelsDragOnDetach => false;

    protected override void OnAttached()
    {
        DragControl = AssociatedObject;
        base.OnAttached();
    }

    protected override Control? OnFindDraggedContainer()
    {
        return AssociatedObject;
    }

    protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
    {
        if (itemsControl?.DataContext is DecoderPriorityPageViewModel viewModel)
        {
            viewModel.MoveItem(oldIndex, newIndex);
        }

        if (itemsControl is SelectingItemsControl selectingItemsControl)
        {
            selectingItemsControl.SelectedIndex = newIndex;
        }
    }
}
