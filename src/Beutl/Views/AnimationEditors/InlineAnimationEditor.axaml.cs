using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;

using Beutl.Animation.Easings;
using Beutl.Controls.Behaviors;
using Beutl.ViewModels.AnimationEditors;
using Beutl.ViewModels;
using Beutl.Views.AnimationVisualizer;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.AnimationEditors;

public partial class InlineAnimationEditor : UserControl
{
    public InlineAnimationEditor()
    {
        InitializeComponent();
        var collection = Interaction.GetBehaviors(this);
        collection.Add(new _DragBehavior()
        {
            Orientation = Orientation.Horizontal,
            DragControl = this
        });
        collection.Add(new _DragDropBehavior());
        collection.Add(new _ResizeBehavior());

        this.SubscribeDataContextChange<InlineAnimationEditorViewModel>(
            OnDataContextAttached,
            OnDataContextDetached);
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<EditView>()?.DataContext is EditViewModel editViewModel
            && DataContext is InlineAnimationEditorViewModel viewModel)
        {
            // 右側のタブを開く
            AnimationTabViewModel anmViewModel
                = editViewModel.FindToolTab<AnimationTabViewModel>()
                    ?? new AnimationTabViewModel();

            anmViewModel.Animation.Value = viewModel.Property;
            anmViewModel.ScrollTo(viewModel.Model);
            editViewModel.OpenToolTab(anmViewModel);
        }
    }

    private void OnDataContextAttached(InlineAnimationEditorViewModel obj)
    {
        presenter.Content = AnimationVisualizerExtensions.CreateAnimationSpanVisualizer(obj.Animation, obj.Model);
    }

    private void OnDataContextDetached(InlineAnimationEditorViewModel obj)
    {
        presenter.Content = null;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        Background = this.FindResource("SubtleFillColorTertiaryBrush") as IBrush;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Background = Brushes.Transparent;
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (AssociatedObject?.DataContext is InlineAnimationEditorViewModel viewModel)
            {
                viewModel.Move(newIndex, oldIndex);
            }
        }
    }

    private sealed class _DragDropBehavior : Behavior<InlineAnimationEditor>
    {
        protected override void OnAttached()
        {
            base.OnAttached();

            if (AssociatedObject != null)
            {
                AssociatedObject.AddHandler(DragDrop.DragOverEvent, OnDragOver);
                AssociatedObject.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
                AssociatedObject.AddHandler(DragDrop.DropEvent, OnDrop);
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();

            if (AssociatedObject != null)
            {
                AssociatedObject.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
                AssociatedObject.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
                AssociatedObject.RemoveHandler(DragDrop.DropEvent, OnDrop);
            }
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains("Easing"))
            {
                SetDropAreaClasses(true);
                e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
                e.Handled = true;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private void OnDragLeave(object? sender, DragEventArgs e)
        {
            SetDropAreaClasses(false);
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            if (e.Data.Get("Easing") is Easing easing
                && AssociatedObject?.DataContext is InlineAnimationEditorViewModel viewModel)
            {
                Point position = e.GetPosition(AssociatedObject);

                if (position.X < 32)
                {
                    viewModel.InsertForward(easing);
                }
                else if (position.X > AssociatedObject.Bounds.Width - 32)
                {
                    viewModel.InsertBackward(easing);
                }
                else
                {
                    viewModel.SetEasing(viewModel.Model.Easing, easing);
                }
                SetDropAreaClasses(false);
                e.Handled = true;
            }
        }

        private void SetDropAreaClasses(bool value)
        {
            AssociatedObject!.leftBorder.Classes.Set("droparea", value);
            AssociatedObject.rightBorder.Classes.Set("droparea", value);
        }
    }

    private sealed class _ResizeBehavior : Behavior<InlineAnimationEditor>
    {
        private bool _enableDrag;
        private Point _start;
        private TimeSpan _oldDuration;

        protected override void OnAttached()
        {
            base.OnAttached();

            if (AssociatedObject != null)
            {
                AssociatedObject.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
                AssociatedObject.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
                AssociatedObject.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
                AssociatedObject.AddHandler(PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Tunnel);
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();

            if (AssociatedObject != null)
            {
                AssociatedObject.RemoveHandler(PointerReleasedEvent, OnReleased);
                AssociatedObject.RemoveHandler(PointerPressedEvent, OnPressed);
                AssociatedObject.RemoveHandler(PointerMovedEvent, OnMoved);
                AssociatedObject.RemoveHandler(PointerCaptureLostEvent, OnCaptureLost);
            }
        }

        private void OnReleased(object? sender, PointerReleasedEventArgs e)
        {
            Released(e);
        }

        private void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(AssociatedObject);
            if (point.Properties.IsLeftButtonPressed
                && AssociatedObject?.Cursor == Cursors.SizeWestEast)
            {
                _enableDrag = true;
                _start = point.Position;
                if (AssociatedObject?.DataContext is InlineAnimationEditorViewModel vm)
                {
                    _oldDuration = vm.Model.Duration;
                }

                e.Handled = true;
            }
        }

        private void OnMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject == null) return;

            Point position = e.GetPosition(AssociatedObject);

            if (!_enableDrag)
            {
                if (position.X < 10
                    || position.X > AssociatedObject.Bounds.Width - 10)
                {
                    AssociatedObject.Cursor = Cursors.SizeWestEast;
                }
                else
                {
                    AssociatedObject.Cursor = null;
                }
            }
            else if (AssociatedObject.DataContext is InlineAnimationEditorViewModel viewModel)
            {
                float scale = viewModel.OptionsProvider.Options.Value.Scale;
                TimeSpan pointerFrame = position.X.ToTimeSpan(scale);
                position = position.WithX(pointerFrame.ToPixel(scale));

                if (AssociatedObject.Cursor == Cursors.SizeWestEast)
                {
                    TimeSpan delta = (pointerFrame - _start.X.ToTimeSpan(scale)); //一時的な移動量

                    viewModel.Model.Duration += delta;

                    _start = position;
                    e.Handled = true;
                }
            }
        }

        private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            Released(e);
        }

        private void Released(RoutedEventArgs e)
        {
            if (_enableDrag)
            {
                _enableDrag = false;
                if (AssociatedObject?.DataContext is InlineAnimationEditorViewModel vm)
                {
                    vm.SetDuration(_oldDuration, AssociatedObject.Bounds.Width.ToTimeSpan(vm.OptionsProvider.Options.Value.Scale));
                    e.Handled = true;
                }
            }
        }
    }
}
