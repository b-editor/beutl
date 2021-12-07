using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;

using BEditorNext.ProjectSystem;
using BEditorNext.ViewModels;

using static BEditorNext.Views.Timeline;

using Setter = Avalonia.Styling.Setter;

namespace BEditorNext.Views;

public partial class TimelineLayer : UserControl
{
    private readonly Avalonia.Animation.Animation _animation = new()
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

    public TimelineLayer()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, Layer_PointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, Layer_PointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, Layer_PointerMoved, RoutingStrategies.Tunnel);
    }

    private TimelineLayerViewModel ViewModel => (TimelineLayerViewModel)DataContext!;

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _timeline = this.FindLogicalAncestorOfType<Timeline>();
    }

    private void Layer_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
    }

    private void Layer_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // View (ViewModel)の位置情報をModelと同期する
        ViewModel.SyncModelToViewModel();
    }

    private void Layer_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_timeline == null || _mouseFlag == MouseFlags.MouseUp)
            return;

        Scene scene = _timeline.ViewModel.Scene;
        Point point = e.GetPosition(this);
        TimelineLayerViewModel vm = ViewModel;
        float scale = scene.TimelineOptions.Scale;
        TimeSpan pointerFrame = point.X.ToTimeSpan(scale);

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
            if (_resizeType == AlignmentX.Right)
            {
                // 右
                ViewModel.Width.Value += move;
            }
            else if (_resizeType == AlignmentX.Left && pointerFrame >= TimeSpan.Zero)
            {
                // 左
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
            _mouseFlag = MouseFlags.MouseDown;
            _layerStartAbs = e.GetPosition(this);
            _layerStartRel = point.Position;
            _timeline.ViewModel.Scene.SelectedItem = ViewModel.Model;


            e.Handled = true;

            _animation.PlaybackDirection = PlaybackDirection.Normal;
            await _animation.RunAsync(border);
            border.Opacity = 0.8;
        }
    }

    private async void Border_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _mouseFlag = MouseFlags.MouseUp;

        _animation.PlaybackDirection = PlaybackDirection.Reverse;
        await _animation.RunAsync(border);
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
}
