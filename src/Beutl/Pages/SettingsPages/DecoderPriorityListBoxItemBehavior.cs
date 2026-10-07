using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Xaml.Interactivity;

using Beutl.ViewModels.SettingsPages;
using Beutl.Views;

namespace Beutl.Pages.SettingsPages;

public sealed class DecoderPriorityListBoxItemBehavior : Behavior<ListBoxItem>
{
    private bool _enableDrag;
    private bool _dragStarted;
    private Point _start;
    private int _draggedIndex;
    private int _targetIndex;
    private ItemsControl? _itemsControl;

    private const double DragThreshold = 3;

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is { })
        {
            AssociatedObject.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
            AssociatedObject.AddHandler(InputElement.PointerCaptureLostEvent, CaptureLost, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if (AssociatedObject is { })
        {
            AssociatedObject.RemoveHandler(InputElement.PointerReleasedEvent, Released);
            AssociatedObject.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
            AssociatedObject.RemoveHandler(InputElement.PointerMovedEvent, Moved);
            AssociatedObject.RemoveHandler(InputElement.PointerCaptureLostEvent, CaptureLost);
        }
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties properties = e.GetCurrentPoint(AssociatedObject).Properties;
        if (properties.IsLeftButtonPressed
            && AssociatedObject?.FindLogicalAncestorOfType<ItemsControl>() is { } itemsControl)
        {
            _itemsControl = itemsControl;
            _enableDrag = true;
            _dragStarted = false;
            _start = e.GetPosition(itemsControl);
            _draggedIndex = -1;
            _targetIndex = -1;

            ReorderDragHelper.SetDraggingPseudoClasses(AssociatedObject, true);

            ResetTransforms(_itemsControl);

            e.Pointer.Capture(AssociatedObject);
        }
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (Equals(e.Pointer.Captured, AssociatedObject))
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                Released();
            }

            e.Pointer.Capture(null);
        }
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        Released();
    }

    private void Released()
    {
        if (!_enableDrag)
        {
            return;
        }

        ResetTransforms(_itemsControl);

        if (_itemsControl is { })
        {
            foreach (Control? container in _itemsControl.GetRealizedContainers())
            {
                ReorderDragHelper.SetDraggingPseudoClasses(container, true);
            }
        }

        if (_dragStarted && _draggedIndex >= 0 && _targetIndex >= 0 && _draggedIndex != _targetIndex)
        {
            MoveDraggedItem(_itemsControl, _draggedIndex, _targetIndex);
        }

        if (_itemsControl is { })
        {
            foreach (Control? container in _itemsControl.GetRealizedContainers())
            {
                ReorderDragHelper.SetDraggingPseudoClasses(container, false);
            }
        }

        if (AssociatedObject is { })
        {
            ReorderDragHelper.SetDraggingPseudoClasses(AssociatedObject, false);
        }

        _draggedIndex = -1;
        _targetIndex = -1;
        _enableDrag = false;
        _dragStarted = false;
        _itemsControl = null;
    }

    private static void ResetTransforms(ItemsControl? itemsControl)
    {
        if (itemsControl?.ItemsSource is null)
        {
            return;
        }

        ReorderDragHelper.ResetTranslateTransforms(itemsControl, itemsControl.ItemsSource);
    }

    private static void MoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
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

    private void Moved(object? sender, PointerEventArgs e)
    {
        PointerPointProperties? properties = e.GetCurrentPoint(AssociatedObject).Properties;
        if (Equals(e.Pointer.Captured, AssociatedObject)
            && properties?.IsLeftButtonPressed == true)
        {
            if (_itemsControl?.ItemsSource is null || AssociatedObject?.RenderTransform is null || !_enableDrag)
            {
                return;
            }

            Point position = e.GetPosition(_itemsControl);
            double delta = position.Y - _start.Y;

            if (!_dragStarted)
            {
                Point diff = _start - position;

                if (Math.Abs(diff.Y) > DragThreshold)
                {
                    _dragStarted = true;
                }
                else
                {
                    return;
                }
            }

            ReorderDragHelper.SetTranslateTransform(AssociatedObject, 0, delta);

            _draggedIndex = _itemsControl.IndexFromContainer(AssociatedObject);
            _targetIndex = -1;

            Rect draggedBounds = AssociatedObject.Bounds;
            double draggedStart = draggedBounds.Y;
            double draggedDeltaStart = draggedBounds.Y + delta;
            double draggedDeltaEnd = draggedBounds.Y + delta + draggedBounds.Height;

            int i = 0;

            foreach (object? _ in _itemsControl.ItemsSource)
            {
                Control? targetContainer = _itemsControl.ContainerFromIndex(i);
                if (targetContainer?.RenderTransform is null || ReferenceEquals(targetContainer, AssociatedObject))
                {
                    i++;
                    continue;
                }

                Rect targetBounds = targetContainer.Bounds;
                double targetStart = targetBounds.Y;
                double targetMid = targetBounds.Y + targetBounds.Height / 2;
                int targetIndex = _itemsControl.IndexFromContainer(targetContainer);

                if (targetStart > draggedStart && draggedDeltaEnd >= targetMid)
                {
                    ReorderDragHelper.SetTranslateTransform(targetContainer, 0, -draggedBounds.Height);

                    _targetIndex = _targetIndex == -1 ? targetIndex :
                        targetIndex > _targetIndex ? targetIndex : _targetIndex;
                }
                else if (targetStart < draggedStart && draggedDeltaStart <= targetMid)
                {
                    ReorderDragHelper.SetTranslateTransform(targetContainer, 0, draggedBounds.Height);

                    _targetIndex = _targetIndex == -1 ? targetIndex :
                        targetIndex < _targetIndex ? targetIndex : _targetIndex;
                }
                else
                {
                    ReorderDragHelper.SetTranslateTransform(targetContainer, 0, 0);
                }

                i++;
            }
        }
    }
}
