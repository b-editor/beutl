using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

using Beutl.ProjectSystem;
using Beutl.ViewModels;

using static Beutl.Views.Timeline;

using Setter = Avalonia.Styling.Setter;

namespace Beutl.Views;

public sealed partial class TimelineLayer : UserControl
{
    private static readonly Avalonia.Animation.Animation s_animation1 = new()
    {
        Duration = TimeSpan.FromSeconds(0.083),
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0),
                Setters =
                {
                    new Setter(OpacityProperty, 1d),
                }
            },
            new KeyFrame
            {
                Cue = new Cue(1),
                Setters =
                {
                    new Setter(OpacityProperty, 0.8),
                }
            }
        }
    };
    private Timeline? _timeline;
    private TimeSpan _pointerPosition;
    private IDisposable? _disposable1;

    public TimelineLayer()
    {
        InitializeComponent();

        BehaviorCollection behaviors = Interaction.GetBehaviors(this);
        behaviors.Add(new _ResizeBehavior());
        behaviors.Add(new _MoveBehavior());

        AddHandler(PointerPressedEvent, Layer_PointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, Layer_PointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, Layer_PointerMoved, RoutingStrategies.Tunnel);
    }

    public Func<TimeSpan> GetClickedTime => () => _pointerPosition;

    private TimelineLayerViewModel ViewModel => (TimelineLayerViewModel)DataContext!;

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _timeline = this.FindLogicalAncestorOfType<Timeline>();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TimelineLayerViewModel viewModel)
        {
            _disposable1?.Dispose();
            viewModel.AnimationRequested = async (args, token) =>
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var animation1 = new Avalonia.Animation.Animation
                    {
                        Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
                        Duration = TimeSpan.FromSeconds(0.67),
                        FillMode = FillMode.Forward,
                        Children =
                        {
                            new KeyFrame()
                            {
                                Cue = new Cue(0),
                                Setters =
                                {
                                    new Setter(MarginProperty, border.Margin)
                                }
                            },
                            new KeyFrame()
                            {
                                Cue = new Cue(1),
                                Setters =
                                {
                                    new Setter(MarginProperty, args.BorderMargin)
                                }
                            }
                        }
                    };
                    var animation2 = new Avalonia.Animation.Animation
                    {
                        Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
                        Duration = TimeSpan.FromSeconds(0.67),
                        FillMode = FillMode.Forward,
                        Children =
                        {
                            new KeyFrame()
                            {
                                Cue = new Cue(0),
                                Setters =
                                {
                                    new Setter(MarginProperty, viewModel.Margin.Value)
                                }
                            },
                            new KeyFrame()
                            {
                                Cue = new Cue(1),
                                Setters =
                                {
                                    new Setter(MarginProperty, args.Margin)
                                }
                            }
                        }
                    };

                    Task task1 = animation1.RunAsync(border, null, token);
                    Task task2 = animation2.RunAsync(this, null, token);
                    await Task.WhenAll(task1, task2);
                });
            };
            _disposable1 = viewModel.Model.GetObservable(Layer.IsEnabledProperty)
                .Subscribe(b => Dispatcher.UIThread.InvokeAsync(() => border.Opacity = b ? 1 : 0.5));
        }
    }

    private void Layer_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ZIndex = 5;
        Focus();
    }

    private async void Layer_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // View (ViewModel)の位置情報をModelと同期する
        await ViewModel.SyncModelToViewModel();
        ZIndex = 0;
    }

    private void Layer_PointerMoved(object? sender, PointerEventArgs e)
    {
        Point point = e.GetPosition(this);
        float scale = ViewModel.Timeline.Options.Value.Scale;
        _pointerPosition = point.X.ToTimeSpan(scale);
    }

    private async void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_timeline == null) return;

        PointerPoint point = e.GetCurrentPoint(border);
        if (point.Properties.IsLeftButtonPressed)
        {
            s_animation1.PlaybackDirection = PlaybackDirection.Normal;
            Task task1 = s_animation1.RunAsync(border, null);

            EditViewModel editorContext = _timeline.ViewModel.EditorContext;
            editorContext.SelectedObject.Value = ViewModel.Model;

            await task1;
            border.Opacity = 0.8;
        }
    }

    private async void Border_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_timeline == null) return;

        s_animation1.PlaybackDirection = PlaybackDirection.Reverse;
        Task task1 = s_animation1.RunAsync(border, null);

        EditViewModel editorContext = _timeline.ViewModel.EditorContext;
        editorContext.SelectedObject.Value = ViewModel.Model;
        await task1;
        border.Opacity = 1;
    }

    private TimeSpan RoundStartTime(TimeSpan time, float scale, bool flag)
    {
        Layer layer = ViewModel.Model;

        if (!flag)
        {
            foreach (Layer item in ViewModel.Scene.Children.GetMarshal().Value)
            {
                if (item != layer)
                {
                    const double ThreadholdPixel = 10;
                    TimeSpan threadhold = ThreadholdPixel.ToTimeSpan(scale);
                    TimeSpan start = item.Start;
                    TimeSpan end = start + item.Length;
                    var startRange = new Media.TimeRange(start - threadhold, threadhold);
                    var endRange = new Media.TimeRange(end - threadhold, threadhold);

                    if (endRange.Contains(time))
                    {
                        return end;
                    }
                    else if (startRange.Contains(time))
                    {
                        return start;
                    }
                }
            }
        }

        return time;
    }

    private sealed class _ResizeBehavior : Behavior<TimelineLayer>
    {
        private Layer? _before;
        private Layer? _after;
        private bool _pressed;
        private AlignmentX _resizeType;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject != null)
            {
                AssociatedObject.AddHandler(PointerMovedEvent, OnPointerMoved);
                AssociatedObject.border.AddHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.AddHandler(PointerReleasedEvent, OnBorderPointerReleased);
                AssociatedObject.border.AddHandler(PointerMovedEvent, OnBorderPointerMoved);
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject != null)
            {
                AssociatedObject.RemoveHandler(PointerMovedEvent, OnPointerMoved);
                AssociatedObject.border.RemoveHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.RemoveHandler(PointerMovedEvent, OnBorderPointerMoved);
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is { ViewModel: { } viewModel } layer)
            {
                Point point = e.GetPosition(layer);
                float scale = viewModel.Timeline.Options.Value.Scale;
                TimeSpan pointerFrame = point.X.ToTimeSpan(scale);

                if (layer._timeline is { } timeline && _pressed)
                {
                    pointerFrame = layer.RoundStartTime(pointerFrame, scale, e.KeyModifiers.HasFlag(KeyModifiers.Control));
                    point = point.WithX(pointerFrame.ToPixel(scale));

                    if (layer.Cursor != Cursors.Arrow && layer.Cursor is { })
                    {
                        double left = viewModel.BorderMargin.Value.Left;

                        if (_resizeType == AlignmentX.Right)
                        {
                            // 右
                            double x = _after == null ? point.X : Math.Min(_after.Start.ToPixel(scale), point.X);
                            viewModel.Width.Value = x - left;
                        }
                        else if (_resizeType == AlignmentX.Left && pointerFrame >= TimeSpan.Zero)
                        {
                            // 左
                            double x = _before == null ? point.X : Math.Max(_before.Range.End.ToPixel(scale), point.X);

                            viewModel.Width.Value += left - x;
                            viewModel.BorderMargin.Value = new Thickness(x, 0, 0, 0);
                        }

                        e.Handled = true;
                    }
                }
            }
        }

        private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is { _timeline: { }, border: { } border, ViewModel: { } viewModel } layer)
            {
                PointerPoint point = e.GetCurrentPoint(layer.border);
                if (point.Properties.IsLeftButtonPressed)
                {
                    _before = viewModel.Model.GetBefore(viewModel.Model.ZIndex, viewModel.Model.Start);
                    _after = viewModel.Model.GetAfter(viewModel.Model.ZIndex, viewModel.Model.Range.End);
                    _pressed = true;

                    if (layer.Cursor != Cursors.Arrow && layer.Cursor is { })
                    {
                        e.Handled = true;
                    }
                }
            }
        }

        private void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _before = null;
            _after = null;
            _pressed = false;
        }

        private void OnBorderPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_pressed && AssociatedObject is { border: { } border } layer)
            {
                Point point = e.GetPosition(border);
                double horizon = point.X;

                // 左右 10px内 なら左右矢印
                if (horizon < 10)
                {
                    layer.Cursor = Cursors.SizeWestEast;
                    _resizeType = AlignmentX.Left;
                }
                else if (horizon > border.Bounds.Width - 10)
                {
                    layer.Cursor = Cursors.SizeWestEast;
                    _resizeType = AlignmentX.Right;
                }
                else
                {
                    layer.Cursor = null;
                    _resizeType = AlignmentX.Center;
                }
            }
        }
    }

    private sealed class _MoveBehavior : Behavior<TimelineLayer>
    {
        private bool _pressed;
        private Point _start;

        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject != null)
            {
                AssociatedObject.AddHandler(PointerMovedEvent, OnPointerMoved);
                AssociatedObject.border.AddHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.AddHandler(PointerReleasedEvent, OnBorderPointerReleased);
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject != null)
            {
                AssociatedObject.RemoveHandler(PointerMovedEvent, OnPointerMoved);
                AssociatedObject.border.RemoveHandler(PointerPressedEvent, OnBorderPointerPressed);
                AssociatedObject.border.RemoveHandler(PointerReleasedEvent, OnBorderPointerReleased);
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (AssociatedObject is { ViewModel: { } viewModel } layer)
            {
                Scene scene = viewModel.Scene;
                Point point = e.GetPosition(layer);
                float scale = viewModel.Timeline.Options.Value.Scale;
                TimeSpan pointerFrame = point.X.ToTimeSpan(scale);

                if (layer._timeline is { } timeline && _pressed)
                {
                    pointerFrame = layer.RoundStartTime(pointerFrame, scale, e.KeyModifiers.HasFlag(KeyModifiers.Control));

                    if (layer.Cursor == Cursors.Arrow || layer.Cursor == null)
                    {
                        TimeSpan newframe = pointerFrame - _start.X.ToTimeSpan(scale);

                        newframe = TimeSpan.FromTicks(Math.Clamp(newframe.Ticks, TimeSpan.Zero.Ticks, scene.Duration.Ticks));

                        viewModel.Margin.Value = new Thickness(
                            0,
                            Math.Max(e.GetPosition(timeline.TimelinePanel).Y - _start.Y, 0),
                            0,
                            0);
                        viewModel.BorderMargin.Value = new Thickness(newframe.ToPixel(scale), 0, 0, 0);

                        e.Handled = true;
                    }
                }
            }
        }

        private void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AssociatedObject is { _timeline: { }, border: { } border } layer)
            {
                PointerPoint point = e.GetCurrentPoint(layer.border);
                if (point.Properties.IsLeftButtonPressed)
                {
                    _pressed = true;
                    _start = point.Position;

                    e.Handled = true;
                }
            }
        }

        private void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _pressed = false;
        }
    }
}
