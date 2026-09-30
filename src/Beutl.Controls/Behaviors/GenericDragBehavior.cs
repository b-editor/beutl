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

#nullable enable

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
            _dragStarted = false;
            if (_activePointer is { } pointer && ReferenceEquals(pointer.Captured, previous))
                pointer.Capture(null);
            Released();
            _activePointer = null;

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

    protected virtual ContentPresenter? OnFindDraggedContainer()
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

            AddTransforms(_itemsControl);

            e.Pointer.Capture(DragControl);
            _activePointer = e.Pointer;
            e.Handled = true;
        }
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (Equals(e.Pointer.Captured, DragControl))
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                Released();
            }

            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        Released();
        e.Handled = true;
    }

    private void Released()
    {
        if (!_enableDrag)
        {
            return;
        }

        RemoveTransforms(_itemsControl);

        if (_itemsControl is { })
        {
            foreach (Control container in _itemsControl.GetRealizedContainers())
            {
                SetDraggingPseudoClasses(container, true);
            }
        }

        if (_dragStarted && _draggedIndex >= 0 && _targetIndex >= 0 && _draggedIndex != _targetIndex)
        {
            OnMoveDraggedItem(_itemsControl, _draggedIndex, _targetIndex);
            OnFinishedDragging();
        }

        if (_itemsControl is { })
        {
            foreach (Control container in _itemsControl.GetRealizedContainers())
            {
                SetDraggingPseudoClasses(container, false);
            }
        }

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

    private static void AddTransforms(ItemsControl? itemsControl)
    {
        if (itemsControl?.ItemsSource is null)
        {
            return;
        }

        int i = 0;

        foreach (object? _ in itemsControl.ItemsSource)
        {
            Control? container = itemsControl.ContainerFromIndex(i);
            if (container is not null)
            {
                SetTranslateTransform(container, 0, 0);
            }

            i++;
        }
    }

    private static void RemoveTransforms(ItemsControl? itemsControl)
    {
        if (itemsControl?.ItemsSource is null)
        {
            return;
        }

        int i = 0;

        foreach (object? _ in itemsControl.ItemsSource)
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
        if (Equals(e.Pointer.Captured, DragControl)
            && properties?.IsLeftButtonPressed == true)
        {
            if (_itemsControl?.ItemsSource is null || _draggedContainer?.RenderTransform is null || !_enableDrag)
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

            foreach (object? _ in _itemsControl.ItemsSource)
            {
                Control? targetContainer = _itemsControl.ContainerFromIndex(i);
                if (targetContainer?.RenderTransform is null || ReferenceEquals(targetContainer, _draggedContainer))
                {
                    i++;
                    continue;
                }

                Rect targetBounds = targetContainer.Bounds;

                double targetStart = orientation == Orientation.Horizontal ? targetBounds.X : targetBounds.Y;

                double targetMid = orientation == Orientation.Horizontal
                    ? targetBounds.X + targetBounds.Width / 2
                    : targetBounds.Y + targetBounds.Height / 2;

                int targetIndex = _itemsControl.IndexFromContainer(targetContainer);

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
                    if (orientation == Orientation.Horizontal)
                    {
                        SetTranslateTransform(targetContainer, 0, 0);
                    }
                    else
                    {
                        SetTranslateTransform(targetContainer, 0, 0);
                    }
                }

                i++;
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
