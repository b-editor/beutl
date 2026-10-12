using System.Collections;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;

namespace Beutl.Controls.Behaviors;

public class GenericDragBehavior : Behavior<Control>
{
    private bool _enableDrag;
    private bool _dragStarted;
    private Point _start;
    private int _draggedIndex;
    private int _targetIndex;
    private ItemsControl? _itemsControl;
    private Control? _draggedContainer;
    private Control? _subscribedDragControl;
    private IPointer? _activePointer;

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<GenericDragBehavior, Orientation>(nameof(Orientation));

    public static readonly StyledProperty<double> HorizontalDragThresholdProperty =
        AvaloniaProperty.Register<GenericDragBehavior, double>(nameof(HorizontalDragThreshold), 3);

    public static readonly StyledProperty<double> VerticalDragThresholdProperty =
        AvaloniaProperty.Register<GenericDragBehavior, double>(nameof(VerticalDragThreshold), 3);

    public static readonly StyledProperty<Control?> DragControlProperty =
        AvaloniaProperty.Register<GenericDragBehavior, Control?>(nameof(DragControl));

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public double HorizontalDragThreshold
    {
        get => GetValue(HorizontalDragThresholdProperty);
        set => SetValue(HorizontalDragThresholdProperty, value);
    }

    public double VerticalDragThreshold
    {
        get => GetValue(VerticalDragThresholdProperty);
        set => SetValue(VerticalDragThresholdProperty, value);
    }

    [ResolveByName]
    public Control? DragControl
    {
        get => GetValue(DragControlProperty);
        set => SetValue(DragControlProperty, value);
    }

    // The options below keep how the reorder behaviors of the app handled the pointer before they derived from
    // this one.

    // Without the capture, moves and the release count while a press has armed the drag.
    protected virtual bool CapturesPointer => true;

    protected virtual DragEventHandling HandledEvents => DragEventHandling.Always;

    // Whether the containers are counted through ItemsControl.Items instead of ItemsSource.
    protected virtual bool CountsItems => false;

    // Whether detaching, or switching DragControl, cancels a drag in progress.
    protected virtual bool CancelsDragOnDetach => true;

    protected override void OnAttached()
    {
        base.OnAttached();
        UpdateDragControlHandlers(DragControl);
    }

    protected override void OnDetaching()
    {
        UpdateDragControlHandlers(null);
        base.OnDetaching();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DragControlProperty && AssociatedObject != null)
        {
            UpdateDragControlHandlers(DragControl);
        }
    }

    private void UpdateDragControlHandlers(Control? control)
    {
        if (ReferenceEquals(_subscribedDragControl, control))
            return;

        if (_subscribedDragControl is { } previous)
        {
            // A control switch cancels the current gesture before its capture-lost handler is removed.
            if (CancelsDragOnDetach)
            {
                _dragStarted = false;
                if (_activePointer is { } pointer && ReferenceEquals(pointer.Captured, previous))
                    pointer.Capture(null);
                Released();
                _activePointer = null;
            }

            previous.RemoveHandler(InputElement.PointerReleasedEvent, Released);
            previous.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
            previous.RemoveHandler(InputElement.PointerMovedEvent, Moved);
            previous.RemoveHandler(InputElement.PointerCaptureLostEvent, CaptureLost);
        }

        _subscribedDragControl = control;
        if (control is null)
            return;

        control.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        control.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        control.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        control.AddHandler(InputElement.PointerCaptureLostEvent, CaptureLost, RoutingStrategies.Tunnel);
    }

    protected virtual Control? OnFindDraggedContainer()
    {
        return AssociatedObject?.FindAncestorOfType<ContentPresenter>();
    }

    protected virtual void OnStartedDragging()
    {
    }

    protected virtual void OnFinishedDragging()
    {
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties properties = e.GetCurrentPoint(AssociatedObject).Properties;
        if (properties.IsLeftButtonPressed
            && AssociatedObject?.FindLogicalAncestorOfType<ItemsControl>() is { } itemsControl)
        {
            _enableDrag = true;
            _dragStarted = false;
            _start = e.GetPosition(itemsControl);
            _draggedIndex = -1;
            _targetIndex = -1;
            _itemsControl = itemsControl;
            _draggedContainer = OnFindDraggedContainer();

            if (_draggedContainer is { })
            {
                SetDraggingPseudoClasses(_draggedContainer, true);
            }

            ResetTransforms(_itemsControl);

            if (CapturesPointer)
            {
                e.Pointer.Capture(DragControl);
                _activePointer = e.Pointer;
            }

            if (ShouldMarkHandled())
                e.Handled = true;
        }
    }

    private bool IsDragPointer(IPointer pointer)
    {
        return CapturesPointer ? Equals(pointer.Captured, DragControl) : _enableDrag;
    }

    // Read before Released() resets the drag.
    private bool ShouldMarkHandled()
    {
        return HandledEvents switch
        {
            DragEventHandling.Always => true,
            DragEventHandling.WhileDragging => _dragStarted,
            _ => false
        };
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (IsDragPointer(e.Pointer))
        {
            bool handled = ShouldMarkHandled();
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                Released();
            }

            if (CapturesPointer)
                e.Pointer.Capture(null);

            if (handled)
                e.Handled = true;
        }
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        bool handled = ShouldMarkHandled();
        Released();
        if (handled)
            e.Handled = true;
    }

    private void Released()
    {
        if (!_enableDrag)
        {
            return;
        }

        ResetTransforms(_itemsControl);

        SetRealizedContainersDragging(_itemsControl, true);

        if (_dragStarted && _draggedIndex >= 0 && _targetIndex >= 0 && _draggedIndex != _targetIndex)
        {
            OnMoveDraggedItem(_itemsControl, _draggedIndex, _targetIndex);
            OnFinishedDragging();
        }

        SetRealizedContainersDragging(_itemsControl, false);

        if (_draggedContainer is { })
        {
            SetDraggingPseudoClasses(_draggedContainer, false);
        }

        _draggedIndex = -1;
        _targetIndex = -1;
        _enableDrag = false;
        _dragStarted = false;
        _itemsControl = null;

        _draggedContainer = null;
        _activePointer = null;
    }

    private IEnumerable? GetItems(ItemsControl? itemsControl)
    {
        return CountsItems ? itemsControl?.Items : itemsControl?.ItemsSource;
    }

    private void ResetTransforms(ItemsControl? itemsControl)
    {
        if (itemsControl is null || GetItems(itemsControl) is not { } items)
        {
            return;
        }

        int i = 0;

        foreach (object? _ in items)
        {
            Control? container = itemsControl.ContainerFromIndex(i);
            if (container is not null)
            {
                SetTranslateTransform(container, 0, 0);
            }

            i++;
        }
    }

    protected virtual void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
    {
        if (itemsControl?.ItemsSource is not IList items)
        {
            return;
        }

        object? draggedItem = items[oldIndex];
        items.RemoveAt(oldIndex);
        items.Insert(newIndex, draggedItem);

        if (itemsControl is SelectingItemsControl selectingItemsControl)
        {
            selectingItemsControl.SelectedIndex = newIndex;
        }
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        PointerPointProperties? properties = e.GetCurrentPoint(AssociatedObject).Properties;
        if (IsDragPointer(e.Pointer)
            && properties?.IsLeftButtonPressed == true)
        {
            if (_itemsControl is null
                || GetItems(_itemsControl) is not { } items
                || _draggedContainer?.RenderTransform is null
                || !_enableDrag)
            {
                return;
            }

            Orientation orientation = Orientation;
            Point position = e.GetPosition(_itemsControl);
            double delta = orientation == Orientation.Horizontal ? position.X - _start.X : position.Y - _start.Y;

            if (!_dragStarted)
            {
                OnStartedDragging();

                Point diff = _start - position;
                double horizontalDragThreshold = HorizontalDragThreshold;
                double verticalDragThreshold = VerticalDragThreshold;

                if (orientation == Orientation.Horizontal)
                {
                    if (Math.Abs(diff.X) > horizontalDragThreshold)
                    {
                        _dragStarted = true;
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    if (Math.Abs(diff.Y) > verticalDragThreshold)
                    {
                        _dragStarted = true;
                    }
                    else
                    {
                        return;
                    }
                }
            }

            if (orientation == Orientation.Horizontal)
            {
                SetTranslateTransform(_draggedContainer, delta, 0);
            }
            else
            {
                SetTranslateTransform(_draggedContainer, 0, delta);
            }

            _draggedIndex = _itemsControl.IndexFromContainer(_draggedContainer);
            _targetIndex = -1;

            Rect draggedBounds = _draggedContainer.Bounds;

            double draggedStart = orientation == Orientation.Horizontal ? draggedBounds.X : draggedBounds.Y;

            double draggedDeltaStart = orientation == Orientation.Horizontal
                ? draggedBounds.X + delta
                : draggedBounds.Y + delta;

            double draggedDeltaEnd = orientation == Orientation.Horizontal
                ? draggedBounds.X + delta + draggedBounds.Width
                : draggedBounds.Y + delta + draggedBounds.Height;

            int i = 0;

            foreach (object? _ in items)
            {
                Control? targetContainer = _itemsControl.ContainerFromIndex(i);
                if (targetContainer?.RenderTransform is null || ReferenceEquals(targetContainer, _draggedContainer))
                {
                    i++;
                    continue;
                }

                ShiftTarget(
                    _itemsControl, targetContainer, orientation, draggedBounds,
                    draggedStart, draggedDeltaStart, draggedDeltaEnd);

                i++;
            }
        }
    }

    private void ShiftTarget(
        ItemsControl itemsControl, Control targetContainer, Orientation orientation, Rect draggedBounds,
        double draggedStart, double draggedDeltaStart, double draggedDeltaEnd)
    {
        Rect targetBounds = targetContainer.Bounds;

        double targetStart = orientation == Orientation.Horizontal ? targetBounds.X : targetBounds.Y;

        double targetMid = orientation == Orientation.Horizontal
            ? targetBounds.X + targetBounds.Width / 2
            : targetBounds.Y + targetBounds.Height / 2;

        int targetIndex = itemsControl.IndexFromContainer(targetContainer);

        if (targetStart > draggedStart && draggedDeltaEnd >= targetMid)
        {
            if (orientation == Orientation.Horizontal)
            {
                SetTranslateTransform(targetContainer, -draggedBounds.Width, 0);
            }
            else
            {
                SetTranslateTransform(targetContainer, 0, -draggedBounds.Height);
            }

            _targetIndex = _targetIndex == -1 ? targetIndex :
                targetIndex > _targetIndex ? targetIndex : _targetIndex;
        }
        else if (targetStart < draggedStart && draggedDeltaStart <= targetMid)
        {
            if (orientation == Orientation.Horizontal)
            {
                SetTranslateTransform(targetContainer, draggedBounds.Width, 0);
            }
            else
            {
                SetTranslateTransform(targetContainer, 0, draggedBounds.Height);
            }

            _targetIndex = _targetIndex == -1 ? targetIndex :
                targetIndex < _targetIndex ? targetIndex : _targetIndex;
        }
        else
        {
            SetTranslateTransform(targetContainer, 0, 0);
        }
    }

    private static void SetRealizedContainersDragging(ItemsControl? itemsControl, bool isDragging)
    {
        if (itemsControl is { })
        {
            foreach (Control container in itemsControl.GetRealizedContainers())
            {
                SetDraggingPseudoClasses(container, isDragging);
            }
        }
    }

    private static void SetDraggingPseudoClasses(Control control, bool isDragging)
    {
        if (isDragging)
        {
            ((IPseudoClasses)control.Classes).Add(":dragging");
        }
        else
        {
            ((IPseudoClasses)control.Classes).Remove(":dragging");
        }
    }

    private static void SetTranslateTransform(Control control, double x, double y)
    {
        var transformBuilder = new TransformOperations.Builder(1);
        transformBuilder.AppendTranslate(x, y);
        control.RenderTransform = transformBuilder.Build();
    }
}

// Which pointer events of a drag GenericDragBehavior marks handled.
public enum DragEventHandling
{
    // The press, the release and the capture loss.
    Always,

    // The release and the capture loss that end a drag past its threshold.
    WhileDragging,

    Never,
}
