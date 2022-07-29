using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Transformation;
using Avalonia.Xaml.Interactivity;

using BeUtl.Animation.Easings;
using BeUtl.Services;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed class AnimationSpanEditorDragBehavior : Behavior<AnimationSpanEditor>
{
    private bool _enableDrag;
    private bool _dragStarted;
    private Point _start;
    private int _draggedIndex;
    private int _targetIndex;
    private ItemsControl? _itemsControl;
    private IControl? _draggedContainer;

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject != null)
        {
            AssociatedObject.dragBorder.AddHandler(InputElement.PointerReleasedEvent, DragBorder_Released, RoutingStrategies.Tunnel);
            AssociatedObject.dragBorder.AddHandler(InputElement.PointerPressedEvent, DragBorder_Pressed, RoutingStrategies.Tunnel);
            AssociatedObject.dragBorder.AddHandler(InputElement.PointerMovedEvent, DragBorder_Moved, RoutingStrategies.Tunnel);
            AssociatedObject.dragBorder.AddHandler(InputElement.PointerCaptureLostEvent, DragBorder_CaptureLost, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if (AssociatedObject != null)
        {
            AssociatedObject.dragBorder.RemoveHandler(InputElement.PointerReleasedEvent, DragBorder_Released);
            AssociatedObject.dragBorder.RemoveHandler(InputElement.PointerPressedEvent, DragBorder_Pressed);
            AssociatedObject.dragBorder.RemoveHandler(InputElement.PointerMovedEvent, DragBorder_Moved);
            AssociatedObject.dragBorder.RemoveHandler(InputElement.PointerCaptureLostEvent, DragBorder_CaptureLost);
        }
    }

    private void DragBorder_Released(object? sender, PointerReleasedEventArgs e)
    {
        DragBorder_Released();

        e.Handled = true;
    }

    private void DragBorder_Pressed(object? sender, PointerPressedEventArgs e)
    {
        _enableDrag = true;
        _dragStarted = false;
        _draggedIndex = -1;
        _targetIndex = -1;
        _itemsControl = AssociatedObject.FindLogicalAncestorOfType<ItemsControl>();
        _start = e.GetPosition(_itemsControl);
        _draggedContainer = AssociatedObject.FindLogicalAncestorOfType<ContentPresenter>();

        AddTransforms(_itemsControl);

        e.Handled = true;
    }

    private void DragBorder_Moved(object? sender, PointerEventArgs e)
    {
        if (_itemsControl?.Items is null || _draggedContainer?.RenderTransform is null || !_enableDrag)
        {
            return;
        }

        Point position = e.GetPosition(_itemsControl);
        double delta = position.Y - _start.Y;

        if (!_dragStarted)
        {
            Point diff = _start - position;
            const int verticalDragThreshold = 3;

            if (Math.Abs(diff.Y) > verticalDragThreshold)
            {
                _dragStarted = true;
            }
            else
            {
                return;
            }
        }

        SetTranslateTransform(_draggedContainer, 0, delta);

        _draggedIndex = _itemsControl.ItemContainerGenerator.IndexFromContainer(_draggedContainer);
        _targetIndex = -1;

        Rect draggedBounds = _draggedContainer.Bounds;
        double draggedStart = draggedBounds.Y;
        double draggedDeltaStart = draggedBounds.Y + delta;
        double draggedDeltaEnd = draggedBounds.Y + delta + draggedBounds.Height;

        int i = 0;

        foreach (object? _ in _itemsControl.Items)
        {
            IControl? targetContainer = _itemsControl.ItemContainerGenerator.ContainerFromIndex(i);
            if (targetContainer?.RenderTransform is null || ReferenceEquals(targetContainer, _draggedContainer))
            {
                i++;
                continue;
            }

            Rect targetBounds = targetContainer.Bounds;
            double targetStart = targetBounds.Y;
            double targetMid = targetBounds.Y + (targetBounds.Height / 2);
            int targetIndex = _itemsControl.ItemContainerGenerator.IndexFromContainer(targetContainer);

            if (targetStart > draggedStart && draggedDeltaEnd >= targetMid)
            {
                SetTranslateTransform(targetContainer, 0, -draggedBounds.Height);

                _targetIndex = _targetIndex == -1 ?
                    targetIndex :
                    targetIndex > _targetIndex ? targetIndex : _targetIndex;
                Debug.WriteLine($"Moved Right {_draggedIndex} -> {_targetIndex}");
            }
            else if (targetStart < draggedStart && draggedDeltaStart <= targetMid)
            {
                SetTranslateTransform(targetContainer, 0, draggedBounds.Height);

                _targetIndex = _targetIndex == -1 ?
                    targetIndex :
                    targetIndex < _targetIndex ? targetIndex : _targetIndex;
                Debug.WriteLine($"Moved Left {_draggedIndex} -> {_targetIndex}");
            }
            else
            {
                SetTranslateTransform(targetContainer, 0, 0);
            }

            i++;
        }

        Debug.WriteLine($"Moved {_draggedIndex} -> {_targetIndex}");

        e.Handled = true;
    }

    private void DragBorder_CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        DragBorder_Released();
    }

    private void DragBorder_Released()
    {
        if (!_enableDrag)
        {
            return;
        }

        RemoveTransforms(_itemsControl);

        if (_dragStarted && _draggedIndex >= 0 && _targetIndex >= 0 && _draggedIndex != _targetIndex)
        {
            Debug.WriteLine($"MoveItem {_draggedIndex} -> {_targetIndex}");
            AssociatedObject?.Move(_targetIndex, _draggedIndex);
        }

        _draggedIndex = -1;
        _targetIndex = -1;
        _enableDrag = false;
        _dragStarted = false;
        _itemsControl = null;
        _draggedContainer = null;
    }

    private static void AddTransforms(ItemsControl? itemsControl)
    {
        if (itemsControl?.Items is null)
        {
            return;
        }

        int i = 0;

        foreach (object _ in itemsControl.Items)
        {
            IControl? container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i);
            if (container is not null)
            {
                SetTranslateTransform(container, 0, 0);
            }

            i++;
        }
    }

    private static void RemoveTransforms(ItemsControl? itemsControl)
    {
        if (itemsControl?.Items is null)
        {
            return;
        }

        int i = 0;

        foreach (object _ in itemsControl.Items)
        {
            IControl container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i);
            if (container is not null)
            {
                SetTranslateTransform(container, 0, 0);
            }

            i++;
        }
    }

    private static void SetTranslateTransform(IControl control, double x, double y)
    {
        var transformBuilder = new TransformOperations.Builder(1);
        transformBuilder.AppendTranslate(x, y);
        control.RenderTransform = transformBuilder.Build();
    }
}

public partial class AnimationSpanEditor : UserControl
{
    public AnimationSpanEditor()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
        Interaction.SetBehaviors(this, new BehaviorCollection
        {
            new AnimationSpanEditorDragBehavior(),
        });

        backgroundBorder.AddHandler(DragDrop.DragOverEvent, BackgroundBorder_DragOver);
        backgroundBorder.AddHandler(DragDrop.DragLeaveEvent, BackgroundBorder_DragLeave);
        topBorder.AddHandler(DragDrop.DropEvent, TopBorder_Drop);
        bottomBorder.AddHandler(DragDrop.DropEvent, BottomBorder_Drop);
        backgroundBorder.AddHandler(DragDrop.DropEvent, BackgroundBorder_Drop);
    }

    private void BackgroundBorder_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing &&
            DataContext is AnimationSpanEditorViewModel vm)
        {
            vm.SetEasing(vm.Model.Easing, easing);
            SetDropAreaClasses(false);
            e.Handled = true;
        }
    }

    private void TopBorder_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing &&
            DataContext is AnimationSpanEditorViewModel vm)
        {
            vm.InsertForward(easing);
            SetDropAreaClasses(false);
            e.Handled = true;
        }
    }

    private void BottomBorder_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing &&
            DataContext is AnimationSpanEditorViewModel vm)
        {
            vm.InsertBackward(easing);
            SetDropAreaClasses(false);
            e.Handled = true;
        }
    }

    private void BackgroundBorder_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("Easing"))
        {
            SetDropAreaClasses(true);
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void BackgroundBorder_DragLeave(object? sender, RoutedEventArgs e)
    {
        SetDropAreaClasses(false);
    }

    private void SetDropAreaClasses(bool value)
    {
        topBorder.Classes.Set("droparea", value);
        bottomBorder.Classes.Set("droparea", value);
    }

    public void Move(int newIndex, int oldIndex)
    {
        if (DataContext is AnimationSpanEditorViewModel viewModel)
        {
            viewModel.Move(newIndex, oldIndex);
        }
    }

    public void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AnimationSpanEditorViewModel viewModel)
        {
            viewModel.RemoveItem();
        }
    }

    private sealed class ViewModelToViewConverter : IValueConverter
    {
        public static readonly ViewModelToViewConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is BaseEditorViewModel viewModel)
            {
                Control? editor = PropertyEditorService.CreateEditor(viewModel.WrappedProperty);

                return editor ?? new Label
                {
                    Height = 24,
                    Margin = new Thickness(0, 4),
                    Content = viewModel.WrappedProperty.AssociatedProperty.Name
                };
            }
            else
            {
                return BindingNotification.Null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingNotification.Null;
        }
    }
}
