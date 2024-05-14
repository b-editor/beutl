using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using Beutl.Animation.Easings;
using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.Views;

public partial class InlineAnimationLayer : UserControl
{
    public InlineAnimationLayer()
    {
        InitializeComponent();
        Height = FrameNumberHelper.LayerHeight;
        this.SubscribeDataContextChange<InlineAnimationLayerViewModel>(
            OnDataContextAttached,
            OnDataContextDetached);

        items.ContainerPrepared += OnMaterialized;
        items.ContainerClearing += OnDematerialized;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrap);
    }

    private void OnDrap(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(KnownLibraryItemFormats.Easing)
            && e.Data.Get(KnownLibraryItemFormats.Easing) is Easing easing
            && DataContext is InlineAnimationLayerViewModel { Timeline: { Options.Value.Scale: { } scale } } viewModel)
        {
            TimeSpan time = e.GetPosition(this).X.ToTimeSpan(scale);
            viewModel.DropEasing(easing, time);
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(KnownLibraryItemFormats.Easing))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnMaterialized(object? sender, ContainerPreparedEventArgs e)
    {
        Interaction.GetBehaviors(e.Container).Add(new _DragBehavior());
    }

    private void OnDematerialized(object? sender, ContainerClearingEventArgs e)
    {
        Interaction.GetBehaviors(e.Container).Clear();
    }

    private void OnDataContextDetached(InlineAnimationLayerViewModel obj)
    {
        obj.AnimationRequested = null;
    }

    private void OnDataContextAttached(InlineAnimationLayerViewModel obj)
    {
        obj.AnimationRequested = async (margin, leftMargin, token) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var animation1 = new Avalonia.Animation.Animation { Easing = new Avalonia.Animation.Easings.SplineEasing(0.1, 0.9, 0.2, 1.0), Duration = TimeSpan.FromSeconds(0.25), FillMode = FillMode.Forward, Children = { new KeyFrame() { Cue = new Cue(0), Setters = { new Setter(MarginProperty, Margin) } }, new KeyFrame() { Cue = new Cue(1), Setters = { new Setter(MarginProperty, margin) } } } };
                var animation2 = new Avalonia.Animation.Animation { Easing = new Avalonia.Animation.Easings.SplineEasing(0.1, 0.9, 0.2, 1.0), Duration = TimeSpan.FromSeconds(0.25), FillMode = FillMode.Forward, Children = { new KeyFrame() { Cue = new Cue(0), Setters = { new Setter(MarginProperty, items.Margin) } }, new KeyFrame() { Cue = new Cue(1), Setters = { new Setter(MarginProperty, leftMargin) } } } };

                Task task1 = animation1.RunAsync(this, token);
                Task task2 = animation2.RunAsync(items, token);
                await Task.WhenAll(task1, task2);
            });
        };
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InlineAnimationLayerViewModel viewModel
            && sender is MenuItem { DataContext: InlineKeyFrameViewModel itemViewModel })
        {
            viewModel.RemoveKeyFrame(itemViewModel.Model);
        }
    }

    private sealed class _DragBehavior : Behavior<Control>
    {
        private bool _pressed;
        private Point _start;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject is InputElement ie)
            {
                ie.PointerPressed += OnPointerPressed;
                ie.PointerReleased += OnPointerReleased;
                ie.PointerMoved += OnPointerMoved;
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject is InputElement ie)
            {
                ie.PointerPressed -= OnPointerPressed;
                ie.PointerReleased -= OnPointerReleased;
                ie.PointerMoved -= OnPointerMoved;
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is { DataContext: InlineKeyFrameViewModel viewModel }
                && _pressed)
            {
                Point position = e.GetPosition(AssociatedObject);
                Point delta = position - _start;
                _start = position;

                viewModel.Left.Value += delta.X;
                e.Handled = true;
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (AssociatedObject is { DataContext: InlineKeyFrameViewModel viewModel }
                && _pressed)
            {
                viewModel.UpdateKeyTime();
                _pressed = false;
                e.Handled = true;
            }
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject != null)
            {
                PointerPoint point = e.GetCurrentPoint(AssociatedObject);

                if (point.Properties.IsLeftButtonPressed)
                {
                    _pressed = true;
                    _start = point.Position;
                    e.Handled = true;
                }
            }
        }
    }
}
