using System.Collections;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Transformation;
using Avalonia.Xaml.Interactivity;

using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.ViewModels;
using BeUtl.ViewModels.AnimationEditors;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.AnimationEditors;

public sealed class AnimationEditorDragBehavior : Behavior<AnimationEditor>
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
        double delta = position.X - _start.X;

        if (!_dragStarted)
        {
            Point diff = _start - position;
            const int horizontalDragThreshold = 3;

            if (Math.Abs(diff.X) > horizontalDragThreshold)
            {
                _dragStarted = true;
            }
            else
            {
                return;
            }
        }

        SetTranslateTransform(_draggedContainer, delta, 0);

        _draggedIndex = _itemsControl.ItemContainerGenerator.IndexFromContainer(_draggedContainer);
        _targetIndex = -1;

        Rect draggedBounds = _draggedContainer.Bounds;
        double draggedStart = draggedBounds.X;
        double draggedDeltaStart = draggedBounds.X + delta;
        double draggedDeltaEnd = draggedBounds.X + delta + draggedBounds.Width;

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
            double targetStart = targetBounds.X;
            double targetMid = targetBounds.X + (targetBounds.Width / 2);
            int targetIndex = _itemsControl.ItemContainerGenerator.IndexFromContainer(targetContainer);

            if (targetStart > draggedStart && draggedDeltaEnd >= targetMid)
            {
                SetTranslateTransform(targetContainer, -draggedBounds.Width, 0);

                _targetIndex = _targetIndex == -1 ?
                    targetIndex :
                    targetIndex > _targetIndex ? targetIndex : _targetIndex;
                Debug.WriteLine($"Moved Right {_draggedIndex} -> {_targetIndex}");
            }
            else if (targetStart < draggedStart && draggedDeltaStart <= targetMid)
            {
                SetTranslateTransform(targetContainer, draggedBounds.Width, 0);

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
            MoveDraggedItem(_itemsControl, _draggedIndex, _targetIndex);
            if (AssociatedObject?.DataContext is AnimationEditorViewModel vm)
            {
                vm.Move(_targetIndex, _draggedIndex);
            }
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

    private static void MoveDraggedItem(ItemsControl? itemsControl, int draggedIndex, int targetIndex)
    {
        if (itemsControl?.Items is not IList items)
        {
            return;
        }

        object? draggedItem = items[draggedIndex];
        items.RemoveAt(draggedIndex);
        items.Insert(targetIndex, draggedItem);
    }

    private static void SetTranslateTransform(IControl control, double x, double y)
    {
        var transformBuilder = new TransformOperations.Builder(1);
        transformBuilder.AppendTranslate(x, y);
        control.RenderTransform = transformBuilder.Build();
    }
}

public sealed class AnimationEditorResizeBehavior : Behavior<AnimationEditor>
{
    private bool _enableDrag;
    private Point _start;
    private TimeSpan _oldDuration;

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject != null)
        {
            AssociatedObject.resizeBorder.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
            AssociatedObject.resizeBorder.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
            AssociatedObject.resizeBorder.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
            AssociatedObject.resizeBorder.AddHandler(InputElement.PointerCaptureLostEvent, CaptureLost, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if (AssociatedObject != null)
        {
            AssociatedObject.resizeBorder.RemoveHandler(InputElement.PointerReleasedEvent, Released);
            AssociatedObject.resizeBorder.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
            AssociatedObject.resizeBorder.RemoveHandler(InputElement.PointerMovedEvent, Moved);
            AssociatedObject.resizeBorder.RemoveHandler(InputElement.PointerCaptureLostEvent, CaptureLost);
        }
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        Released();
        e.Handled = true;
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        _enableDrag = true;
        _start = e.GetPosition(AssociatedObject);
        if (AssociatedObject?.DataContext is AnimationEditorViewModel vm)
        {
            _oldDuration = vm.Model.Duration;
        }

        e.Handled = true;
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (AssociatedObject == null || !_enableDrag) return;

        Point position = e.GetPosition(AssociatedObject);
        double delta = position.X - _start.X;

        AssociatedObject.Width += delta;

        _start = position;
        e.Handled = true;
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        Released();
        e.Handled = true;
    }

    private void Released()
    {
        _enableDrag = false;
        if (AssociatedObject?.DataContext is AnimationEditorViewModel vm)
        {
            vm.SetDuration(_oldDuration, AssociatedObject.Bounds.Width.ToTimeSpan(vm.OptionsProvider.Options.Value.Scale));
        }
    }
}

public partial class AnimationEditor : UserControl
{
    public AnimationEditor()
    {
        InitializeComponent();
        Interaction.SetBehaviors(this, new BehaviorCollection
        {
            new AnimationEditorDragBehavior(),
            new AnimationEditorResizeBehavior(),
        });
        backgroundBorder.AddHandler(DragDrop.DragOverEvent, BackgroundBorder_DragOver);
        backgroundBorder.AddHandler(DragDrop.DragLeaveEvent, BackgroundBorder_DragLeave);
        leftBorder.AddHandler(DragDrop.DropEvent, LeftBorder_Drop);
        rightBorder.AddHandler(DragDrop.DropEvent, RightBorder_Drop);
        backgroundBorder.AddHandler(DragDrop.DropEvent, BackgroundBorder_Drop);
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<EditView>().DataContext is EditViewModel editViewModel
            && DataContext is AnimationEditorViewModel viewModel)
        {
            // 右側のタブを開く
            AnimationTabViewModel anmViewModel
                = editViewModel.FindToolTab<AnimationTabViewModel>()
                    ?? new AnimationTabViewModel();

            anmViewModel.Animation.Value = viewModel.WrappedProperty;
            anmViewModel.ScrollTo(viewModel.Model);
            editViewModel.OpenToolTab(anmViewModel);
        }
    }

    private void BackgroundBorder_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing &&
            DataContext is AnimationEditorViewModel vm)
        {
            vm.SetEasing(vm.Model.Easing, easing);
            SetDropAreaClasses(false);
            e.Handled = true;
        }
    }

    private void LeftBorder_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing &&
            DataContext is AnimationEditorViewModel vm)
        {
            vm.InsertForward(easing);
            SetDropAreaClasses(false);
            e.Handled = true;
        }
    }

    private void RightBorder_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing &&
            DataContext is AnimationEditorViewModel vm)
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
        leftBorder.Classes.Set("droparea", value);
        rightBorder.Classes.Set("droparea", value);
    }
}
