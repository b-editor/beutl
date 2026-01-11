using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using Beutl.ViewModels;
using KeyFrame = Avalonia.Animation.KeyFrame;

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
        if (DataContext is not InlineAnimationLayerViewModel viewModel) return;

        if (viewModel.HandleDrop(e, e.GetPosition(this).X))
        {
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not InlineAnimationLayerViewModel viewModel) return;

        if (viewModel.HandleDragOver(e))
        {
            e.Handled = true;
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
                var animation1 = new Avalonia.Animation.Animation
                {
                    Easing = new Avalonia.Animation.Easings.SplineEasing(0.1, 0.9, 0.2, 1.0),
                    Duration = TimeSpan.FromSeconds(0.25),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame() { Cue = new Cue(0), Setters = { new Setter(MarginProperty, Margin) } },
                        new KeyFrame() { Cue = new Cue(1), Setters = { new Setter(MarginProperty, margin) } }
                    }
                };
                var animation2 = new Avalonia.Animation.Animation
                {
                    Easing = new Avalonia.Animation.Easings.SplineEasing(0.1, 0.9, 0.2, 1.0),
                    Duration = TimeSpan.FromSeconds(0.25),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame()
                        {
                            Cue = new Cue(0), Setters = { new Setter(MarginProperty, items.Margin) }
                        },
                        new KeyFrame()
                        {
                            Cue = new Cue(1), Setters = { new Setter(MarginProperty, leftMargin) }
                        }
                    }
                };

                Task task1 = animation1.RunAsync(this, token);
                Task task2 = animation2.RunAsync(items, token);
                await Task.WhenAll(task1, task2);
            });
        };
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (DataContext is not InlineAnimationLayerViewModel viewModel) return;
        Point point = e.GetPosition(this);
        viewModel.UpdatePointerPosition(point.X);
    }

    private sealed class _DragBehavior : Behavior<Control>
    {
        private bool _pressed;
        private Point _start;
        private InlineKeyFrameViewModel[]? _items;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject is not InputElement ie) return;

            ie.PointerPressed += OnPointerPressed;
            ie.PointerReleased += OnPointerReleased;
            ie.PointerMoved += OnPointerMoved;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject is not InputElement ie) return;

            ie.PointerPressed -= OnPointerPressed;
            ie.PointerReleased -= OnPointerReleased;
            ie.PointerMoved -= OnPointerMoved;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is not { DataContext: InlineKeyFrameViewModel viewModel }) return;
            if (!_pressed) return;

            Point position = e.GetPosition(AssociatedObject);
            Point delta = position - _start;
            _start = position;

            viewModel.Left.Value += delta.X;
            e.Handled = true;

            if (_items == null) return;
            foreach (InlineKeyFrameViewModel item in _items)
            {
                item.Left.Value += delta.X;
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (AssociatedObject is not { DataContext: InlineKeyFrameViewModel viewModel }) return;
            if (!_pressed) return;

            if (_items != null)
            {
                foreach (InlineKeyFrameViewModel item in _items)
                {
                    item.ReflectModelKeyTime();
                }
                viewModel.ReflectModelKeyTime();
                var history = viewModel.Timeline.EditorContext.HistoryManager;
                history.Commit(CommandNames.MoveKeyFrame);
            }
            else
            {
                viewModel.UpdateKeyTime();
            }

            _items = null;
            _pressed = false;
            e.Handled = true;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is not { DataContext: InlineKeyFrameViewModel viewModel }) return;

            PointerPoint point = e.GetCurrentPoint(AssociatedObject);

            if (!point.Properties.IsLeftButtonPressed) return;

            _pressed = true;
            _start = point.Position;
            e.Handled = true;

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                _items = viewModel.Parent.Items.Where(i => i != viewModel).ToArray();
            }
        }
    }
}
