using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;

using BeUtl.ProjectSystem;
using BeUtl.ViewModels;

using static BeUtl.Views.Timeline;

using Setter = Avalonia.Styling.Setter;

namespace BeUtl.Views;

public partial class TimelineLayer : UserControl
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
    private MouseFlags _mouseFlag = MouseFlags.MouseUp;
    private AlignmentX _resizeType = AlignmentX.Center;
    private Point _layerStartAbs;
    private Point _layerStartRel;
    private TimeSpan _pointerPosition;
    private Layer? _before;
    private Layer? _after;
    private IDisposable? _disposable1;

    public TimelineLayer()
    {
        InitializeComponent();
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
                                    new Setter(MarginProperty, Margin)
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
        Focus();
    }

    private void Layer_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // View (ViewModel)の位置情報をModelと同期する
        ViewModel.SyncModelToViewModel();
    }

    private void Layer_PointerMoved(object? sender, PointerEventArgs e)
    {
        Scene scene = ViewModel.Scene;
        Point point = e.GetPosition(this);
        float scale = ViewModel.Timeline.Options.Value.Scale;
        TimeSpan pointerFrame = point.X.ToTimeSpan(scale);
        _pointerPosition = pointerFrame;

        if (_timeline == null || _mouseFlag == MouseFlags.MouseUp)
            return;

        TimelineLayerViewModel vm = ViewModel;
        pointerFrame = RoundStartTime(pointerFrame, scale, e.KeyModifiers.HasFlag(KeyModifiers.Control));
        point = point.WithX(pointerFrame.ToPixel(scale));

        if (Cursor == Cursors.Arrow || Cursor == null)
        {
            TimeSpan newframe = pointerFrame - _layerStartRel.X.ToTimeSpan(scale);

            newframe = TimeSpan.FromTicks(Math.Clamp(newframe.Ticks, TimeSpan.Zero.Ticks, scene.Duration.Ticks));

            vm.Margin.Value = new Thickness(
                0,
                Math.Max(e.GetPosition(_timeline.TimelinePanel).Y - _layerStartRel.Y, 0),
                0,
                0);
            vm.BorderMargin.Value = new Thickness(newframe.ToPixel(scale), 0, 0, 0);
        }
        else
        {
            double move = (pointerFrame - _layerStartAbs.X.ToTimeSpan(scale)).ToPixel(scale); //一時的な移動量
            double width = ViewModel.Width.Value;
            double left = ViewModel.BorderMargin.Value.Left;

            if (_resizeType == AlignmentX.Right)
            {
                // 右
                double right = width + left;
                move = _after == null ? move : (Math.Min(_after.Start.ToPixel(scale), right + move) - right);
                ViewModel.Width.Value += move;
            }
            else if (_resizeType == AlignmentX.Left && pointerFrame >= TimeSpan.Zero)
            {
                // 左
                move = Math.Max(_before?.Range.End.ToPixel(scale) ?? 0, left + move) - left;
                ViewModel.Width.Value -= move;
                ViewModel.BorderMargin.Value += new Thickness(move, 0, 0, 0);
            }
        }

        _layerStartAbs = point;
        e.Handled = true;
    }

    private async void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_timeline == null) return;

        PointerPoint point = e.GetCurrentPoint(border);
        if (point.Properties.IsLeftButtonPressed)
        {
            _before = ViewModel.Model.GetBefore(ViewModel.Model.ZIndex, ViewModel.Model.Start);
            _after = ViewModel.Model.GetAfter(ViewModel.Model.ZIndex, ViewModel.Model.Range.End);
            Task task1 = s_animation1.RunAsync(border, null);

            _mouseFlag = MouseFlags.MouseDown;
            _layerStartAbs = e.GetPosition(this);
            _layerStartRel = point.Position;
            EditViewModel editorContext = _timeline.ViewModel.EditorContext;
            editorContext.SelectedObject.Value = ViewModel.Model;

            e.Handled = true;

            s_animation1.PlaybackDirection = PlaybackDirection.Normal;
            await task1;
            border.Opacity = 0.8;
        }
    }

    private async void Border_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_timeline == null) return;

        _mouseFlag = MouseFlags.MouseUp;
        s_animation1.PlaybackDirection = PlaybackDirection.Reverse;
        Task task1 = s_animation1.RunAsync(border, null);

        EditViewModel editorContext = _timeline.ViewModel.EditorContext;
        editorContext.SelectedObject.Value = ViewModel.Model;
        await task1;
        border.Opacity = 1;
    }

    private void Border_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_mouseFlag == MouseFlags.MouseDown) return;

        Point point = e.GetPosition(border);
        double horizon = point.X;

        // 左右 10px内 なら左右矢印
        if (horizon < 10)
        {
            Cursor = Cursors.SizeWestEast;
            _resizeType = AlignmentX.Left;
        }
        else if (horizon > border.Bounds.Width - 10)
        {
            Cursor = Cursors.SizeWestEast;
            _resizeType = AlignmentX.Right;
        }
        else
        {
            Cursor = null;
            _resizeType = AlignmentX.Center;
        }
    }

    private TimeSpan RoundStartTime(TimeSpan time, float scale, bool flag)
    {
        Layer layer = ViewModel.Model;

        if (!flag)
        {
            foreach (Layer item in ViewModel.Scene.Children.AsSpan())
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
}
