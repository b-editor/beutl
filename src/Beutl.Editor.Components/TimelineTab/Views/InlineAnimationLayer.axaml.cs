using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using Beutl.Animation;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Language;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.TimelineTab.Views;

public partial class InlineAnimationLayer : UserControl
{
    public InlineAnimationLayer()
    {
        InitializeComponent();
        this.SubscribeDataContextChange<InlineAnimationLayerViewModel>(
            OnDataContextAttached,
            OnDataContextDetached);

        items.ContainerPrepared += OnMaterialized;
        items.ContainerClearing += OnDematerialized;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDrop(object? sender, DragEventArgs e)
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
                var animation1 = TimelineSettleAnimation.Create(
                    [new Setter(MarginProperty, Margin)],
                    [new Setter(MarginProperty, margin)]);
                var animation2 = TimelineSettleAnimation.Create(
                    [new Setter(MarginProperty, items.Margin)],
                    [new Setter(MarginProperty, leftMargin)]);

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
        private DragSession? _drag;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject is not InputElement ie) return;

            ie.PointerPressed += OnPointerPressed;
            ie.PointerReleased += OnPointerReleased;
            ie.PointerMoved += OnPointerMoved;
            ie.PointerCaptureLost += OnPointerCaptureLost;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            _drag?.Confirm();
            if (AssociatedObject is not InputElement ie) return;

            ie.PointerPressed -= OnPointerPressed;
            ie.PointerReleased -= OnPointerReleased;
            ie.PointerMoved -= OnPointerMoved;
            ie.PointerCaptureLost -= OnPointerCaptureLost;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is not { DataContext: InlineKeyFrameViewModel viewModel } control) return;
            if (!_pressed) return;

            // The first move opens the DragSession; null means another gesture holds history.
            _drag ??= DragSession.Begin(
                viewModel.Timeline.EditorContext, control, CommandNames.MoveKeyFrame, ConfirmDrag, CancelDrag);
            if (_drag is null)
            {
                CancelDrag();
                return;
            }

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
            if (AssociatedObject is not { DataContext: InlineKeyFrameViewModel }) return;
            if (!_pressed) return;

            e.Handled = true;
            if (_drag is { } drag)
                drag.Confirm();
            else
                ConfirmDrag();
        }

        // A drag in progress confirms through its DragSession; a press that never moved has nothing to keep.
        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (_pressed && _drag is null)
                CancelDrag();
        }

        // Escape, Undo and a clip locked during the drag put the keyframes back where the model has them.
        private void CancelDrag()
        {
            _drag = null;
            _pressed = false;
            InlineKeyFrameViewModel[]? items = _items;
            _items = null;
            if (AssociatedObject is not { DataContext: InlineKeyFrameViewModel viewModel }) return;

            viewModel.RestoreVisualFromModel();
            if (items == null) return;
            foreach (InlineKeyFrameViewModel item in items)
            {
                item.RestoreVisualFromModel();
            }
        }

        // Release, capture loss, playback start and detach keep the keyframes where the drag reached.
        private void ConfirmDrag()
        {
            if (AssociatedObject is not { DataContext: InlineKeyFrameViewModel viewModel } || !viewModel.Parent.IsEditable)
            {
                CancelDrag();
                return;
            }

            _drag = null;
            _pressed = false;
            InlineKeyFrameViewModel[]? items = _items;
            _items = null;
            if (items != null)
            {
                foreach (InlineKeyFrameViewModel item in items)
                {
                    item.ReflectModelKeyTime();
                }
                viewModel.ReflectModelKeyTime();
                viewModel.Timeline.EditorContext.GetRequiredService<HistoryManager>()
                    .Commit(CommandNames.MoveKeyFrame);
            }
            else
            {
                viewModel.UpdateKeyTime();
            }
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is not { DataContext: InlineKeyFrameViewModel viewModel }) return;
            if (!viewModel.Parent.IsEditable) return;

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
