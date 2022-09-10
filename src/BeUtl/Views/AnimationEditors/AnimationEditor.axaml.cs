using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Xaml.Interactivity;

using BeUtl.Animation.Easings;
using BeUtl.Controls.Behaviors;
using BeUtl.ViewModels;
using BeUtl.ViewModels.AnimationEditors;
using BeUtl.ViewModels.Tools;

namespace BeUtl.Views.AnimationEditors;

public partial class AnimationEditor : UserControl
{
    public AnimationEditor()
    {
        InitializeComponent();
        Interaction.SetBehaviors(this, new BehaviorCollection
        {
            new _DragBehavior()
            {
                Orientation = Orientation.Horizontal,
                DragControl = dragBorder
            },
            new _ResizeBehavior(),
        });
        backgroundBorder.AddHandler(DragDrop.DragOverEvent, BackgroundBorder_DragOver);
        backgroundBorder.AddHandler(DragDrop.DragLeaveEvent, BackgroundBorder_DragLeave);
        leftBorder.AddHandler(DragDrop.DropEvent, LeftBorder_Drop);
        rightBorder.AddHandler(DragDrop.DropEvent, RightBorder_Drop);
        backgroundBorder.AddHandler(DragDrop.DropEvent, BackgroundBorder_Drop);
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<EditView>()?.DataContext is EditViewModel editViewModel
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

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (AssociatedObject?.DataContext is AnimationEditorViewModel viewModel)
            {
                viewModel.Move(newIndex, oldIndex);
            }
        }
    }

    private sealed class _ResizeBehavior : Behavior<AnimationEditor>
    {
        private bool _enableDrag;
        private Point _start;
        private TimeSpan _oldDuration;

        protected override void OnAttached()
        {
            base.OnAttached();

            if (AssociatedObject != null)
            {
                AssociatedObject.resizeBorder.AddHandler(PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
                AssociatedObject.resizeBorder.AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
                AssociatedObject.resizeBorder.AddHandler(PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
                AssociatedObject.resizeBorder.AddHandler(PointerCaptureLostEvent, CaptureLost, RoutingStrategies.Tunnel);
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();

            if (AssociatedObject != null)
            {
                AssociatedObject.resizeBorder.RemoveHandler(PointerReleasedEvent, Released);
                AssociatedObject.resizeBorder.RemoveHandler(PointerPressedEvent, Pressed);
                AssociatedObject.resizeBorder.RemoveHandler(PointerMovedEvent, Moved);
                AssociatedObject.resizeBorder.RemoveHandler(PointerCaptureLostEvent, CaptureLost);
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
}
