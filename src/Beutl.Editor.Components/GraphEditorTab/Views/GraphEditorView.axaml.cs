using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Xaml.Interactivity;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings.Extensions;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;
using Vector = Avalonia.Vector;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private TimelineHelper.MouseFlags _mouseFlag = TimelineHelper.MouseFlags.Free;
    private TimeSpan _initialStart;
    private TimeSpan _initialDuration;
    private TimeSpan _pointerFrame;

    public GraphEditorView()
    {
        InitializeComponent();
        scale.PointerMoved += OnContentPointerMoved;
        scale.PointerReleased += OnContentPointerReleased;
        scale.PointerPressed += OnContentPointerPressed;
        background.PointerMoved += OnContentPointerMoved;
        background.PointerReleased += OnContentPointerReleased;
        background.PointerPressed += OnContentPointerPressed;
        graphPanel.PointerMoved += OnGraphPanelPointerMoved;
        graphPanel.PointerReleased += OnGraphPanelPointerReleased;

        scale.AddHandler(PointerWheelChangedEvent, OnContentPointerWheelChanged, RoutingStrategies.Tunnel);
        graphPanel.AddHandler(PointerWheelChangedEvent, OnContentPointerWheelChanged, RoutingStrategies.Tunnel);
        verticalScale.AddHandler(PointerWheelChangedEvent, OnVerticalScalePointerWheelChanged,
            RoutingStrategies.Tunnel);

        this.SubscribeDataContextChange<GraphEditorViewModel>(
            OnDataContextAttached,
            OnDataContextDetached);

        views.ContainerPrepared += OnContainerPrepared;
        views.ContainerClearing += OnContainerClearing;
        Interaction.GetBehaviors(this).Add(new GraphEditorDragDropBehavior());
    }

    public ControlPointMoveState? ControlPointMoveState { get; set; }

    public KeyTimeMoveState? KeyTimeMoveState { get; set; }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is { DataContext: GraphEditorViewViewModel viewModel } container)
        {
            container.Bind(ZIndexProperty, viewModel.IsSelected.Select(v => v ? 1 : 0));
        }
    }

    private void OnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is { } container)
        {
            container.Bind(ZIndexProperty, Observable.ReturnThenNever(BindingValue<int>.Unset));
        }
    }

    private void OnDataContextDetached(GraphEditorViewModel obj)
    {
        _disposables.Clear();
    }

    private void OnDataContextAttached(GraphEditorViewModel obj)
    {
        obj.MinHeight
            .CombineLatest(scroll.GetObservable(BoundsProperty))
            .ObserveOnUIDispatcher()
            .Subscribe(v => graphPanel.Height = Math.Max(v.First, v.Second.Height))
            .DisposeWith(_disposables);

        obj.CurrentTime
            .ObserveOnUIDispatcher()
            .Subscribe(time => OnCurrentTimeChangedForAutoScroll(obj, time))
            .DisposeWith(_disposables);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is GraphEditorViewModel viewModel)
        {
            viewModel.ScrollOffset.Subscribe(offset => scroll.Offset = offset)
                .DisposeWith(_disposables);
        }

        scroll.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            viewModel.ScrollOffset.Value = scroll.Offset;
        }
    }

    private void OnCurrentTimeChangedForAutoScroll(GraphEditorViewModel viewModel, TimeSpan currentTime)
    {
        var mode = GlobalConfiguration.Instance.EditorConfig.TimelineAutoScrollMode;
        if (mode == TimelineAutoScrollMode.None) return;

        var previewPlayer = viewModel.EditorContext.GetService<IPreviewPlayer>();
        if (previewPlayer == null || !previewPlayer.IsPlaying.Value) return;

        float scale = viewModel.Options.Value.Scale;
        double seekBarPixel = currentTime.TimeToPixel(scale);

        double? newOffsetX = TimelineHelper.CalculateAutoScrollOffset(
            seekBarPixel, scroll.Viewport.Width, scroll.Offset.X, mode);

        if (newOffsetX is not { } offsetX) return;

        scroll.Offset = new Vector(offsetX, scroll.Offset.Y);
    }

    private static float Zoom(float delta, float scale)
    {
        const float ZoomSpeed = 1.2f;
        float realDelta = MathF.Sign(delta) * MathF.Abs(delta);

        return MathF.Pow(ZoomSpeed, realDelta) * scale;
    }

    private static double Zoom(double delta, double scale)
    {
        const double ZoomSpeed = 1.2;
        double realDelta = Math.Sign(delta) * Math.Abs(delta);

        return Math.Pow(ZoomSpeed, realDelta) * scale;
    }

    private void UpdateHorizontalZoom(PointerWheelEventArgs e, ref float scale, ref Vector2 offset)
    {
        float oldScale = scale;
        Point pointerPos = e.GetCurrentPoint(graphPanel).Position;
        double deltaLeft = pointerPos.X - offset.X;

        float delta = (float)e.Delta.Y;
        scale = Math.Min(Zoom(delta, scale), 2);

        offset.X = (float)((pointerPos.X / oldScale * scale) - deltaLeft);
    }

    private void OnContentPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            Avalonia.Vector aOffset = scroll.Offset;
            Avalonia.Vector edelta = e.Delta;
            float scale = viewModel.Options.Value.Scale;
            var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);

            if (OperatingSystem.IsWindows() && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                edelta = new Avalonia.Vector(edelta.Y, edelta.X);
            }

            var zoomModifier = KeyGestureHelper.GetCommandModifier();
            if (e.KeyModifiers == zoomModifier)
            {
                // 目盛りのスケールを変更
                UpdateHorizontalZoom(e, ref scale, ref offset);
            }
            else if (e.KeyModifiers.HasAllFlags(zoomModifier | KeyModifiers.Shift))
            {
                double oldScale = viewModel.ScaleY.Value;
                double scaleY = Zoom(edelta.X, oldScale);
                // double scaleY = Zoom(delta.Y, oldScale);
                scaleY = Math.Clamp(scaleY, 0.01, 8.75);

                //offset.Y *= scaleY;
                viewModel.ScaleY.Value = scaleY;
            }
            else
            {
                if (GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection)
                {
                    offset.Y -= (float)(edelta.Y * 50);
                    offset.X -= (float)(edelta.X * 50);
                }
                else
                {
                    // オフセット(X) をスクロール
                    offset.X -= (float)(edelta.Y * 50);
                    offset.Y -= (float)(edelta.X * 50);
                }
            }

            Vector2 originalOffset = viewModel.Options.Value.Offset;
            viewModel.Options.Value = viewModel.Options.Value with
            {
                Scale = scale,
                Offset = new Vector2(offset.X, originalOffset.Y)
            };

            viewModel.ScrollOffset.Value = new(offset.X, offset.Y);

            double delta = aOffset.Y - Math.Max(0, offset.Y);
            if (ControlPointMoveState != null)
            {
                ControlPointMoveState.DragStart += new Point(0, delta);
            }
            else if (KeyTimeMoveState != null)
            {
                KeyTimeMoveState.DragStart += new Point(0, delta);
            }

            e.Handled = true;
        }
    }

    private void OnVerticalScalePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            Avalonia.Vector aOffset = scroll.Offset;
            Avalonia.Vector edelta = e.Delta;
            float scale = viewModel.Options.Value.Scale;
            var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);

            var zoomModifier = KeyGestureHelper.GetCommandModifier();
            if (e.KeyModifiers.HasFlag(zoomModifier))
            {
                double oldScale = viewModel.ScaleY.Value;
                double scaleY = Zoom(edelta.Y, oldScale);
                // double scaleY = Zoom(delta.Y, oldScale);
                scaleY = Math.Clamp(scaleY, 0.01, 8.75);

                //offset.Y *= scaleY;
                viewModel.ScaleY.Value = scaleY;
            }
            else
            {
                if (GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection)
                {
                    offset.X -= (float)(edelta.Y * 50);
                    offset.Y -= (float)(edelta.X * 50);
                }
                else
                {
                    // オフセット(X) をスクロール
                    offset.Y -= (float)(edelta.Y * 50);
                    offset.X -= (float)(edelta.X * 50);
                }
            }

            Vector2 originalOffset = viewModel.Options.Value.Offset;
            viewModel.Options.Value = viewModel.Options.Value with
            {
                Scale = scale,
                Offset = new Vector2(offset.X, originalOffset.Y)
            };

            viewModel.ScrollOffset.Value = new(offset.X, offset.Y);

            double delta = aOffset.Y - Math.Max(0, offset.Y);
            if (ControlPointMoveState != null)
            {
                ControlPointMoveState.DragStart += new Point(0, delta);
            }
            else if (KeyTimeMoveState != null)
            {
                KeyTimeMoveState.DragStart += new Point(0, delta);
            }

            e.Handled = true;
        }
    }

    private void OnContentPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);
            viewModel.UpdatePointerPosition(pointerPt.Position.X);
            int rate = viewModel.Scene.FindHierarchicalParent<Project>().GetFrameRate();
            _pointerFrame = pointerPt.Position.X.PixelToTimeSpan(viewModel.Options.Value.Scale).RoundToRate(rate);

            if (_pointerFrame < TimeSpan.Zero)
            {
                _pointerFrame = TimeSpan.Zero;
            }

            if (_mouseFlag == TimelineHelper.MouseFlags.SeekBarPressed)
            {
                viewModel.CurrentTime.Value = _pointerFrame;
                e.Handled = true;
            }
            else if (_mouseFlag == TimelineHelper.MouseFlags.EndingBarMarkerPressed)
            {
                // ポインタ位置に基づいてシーンDurationを更新
                TimeSpan newDuration = _pointerFrame - viewModel.Scene.Start;
                if (newDuration < TimeSpan.Zero)
                {
                    newDuration = TimeSpan.FromSeconds(1d / rate);
                }

                // 直接値を更新（コマンド記録なし）
                viewModel.Scene.Duration = newDuration;
                e.Handled = true;
            }
            else if (_mouseFlag == TimelineHelper.MouseFlags.StartingBarMarkerPressed)
            {
                TimeSpan newStart = _pointerFrame;
                if (newStart < TimeSpan.Zero)
                {
                    newStart = TimeSpan.Zero;
                }
                else if (newStart > _initialDuration + _initialStart)
                {
                    newStart = _initialDuration + _initialStart - TimeSpan.FromSeconds(1d / rate);
                }

                viewModel.Scene.Start = newStart;
                viewModel.Scene.Duration = _initialDuration + _initialStart - newStart;
                e.Handled = true;
            }
            else
            {
                Point posScale = e.GetPosition(scale);
                double startingBarX = viewModel.StartingBarMargin.Value.Left;
                double endingBarX = viewModel.EndingBarMargin.Value.Left;

                // EndingBarマーカーの当たり判定チェック
                if (TimelineHelper.IsPointInTimelineScaleMarker(pointerPt.Position.X, posScale.Y, startingBarX,
                        endingBarX))
                {
                    scale.Cursor = new Cursor(StandardCursorType.SizeWestEast);
                }
                else
                {
                    scale.Cursor = Cursor.Default;
                }
            }
        }
    }

    private void OnContentPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not GraphEditorViewModel viewModel) return;
        PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);

        if (pointerPt.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            if (_mouseFlag == TimelineHelper.MouseFlags.EndingBarMarkerPressed)
            {
                viewModel.HistoryManager.Commit(CommandNames.ChangeSceneDuration);
            }
            else if (_mouseFlag == TimelineHelper.MouseFlags.StartingBarMarkerPressed)
            {
                viewModel.HistoryManager.Commit(CommandNames.ChangeSceneStart);
            }

            _mouseFlag = TimelineHelper.MouseFlags.Free;
        }
    }

    private void OnContentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);

            if (pointerPt.Properties.IsLeftButtonPressed)
            {
                double endingBarX = viewModel.EndingBarMargin.Value.Left;
                double startingBarX = viewModel.StartingBarMargin.Value.Left;
                Point scalePoint = e.GetPosition(scale);

                // マーカーの当たり判定チェック - TimelineScaleのマーカーのみ
                if (TimelineHelper.IsPointInTimelineScaleEndingMarker(pointerPt.Position.X, scalePoint.Y, endingBarX))
                {
                    _mouseFlag = TimelineHelper.MouseFlags.EndingBarMarkerPressed;
                    _initialDuration = viewModel.Scene.Duration; // 初期値を保存
                }
                else if (TimelineHelper.IsPointInTimelineScaleStartingMarker(pointerPt.Position.X, scalePoint.Y,
                             startingBarX))
                {
                    _mouseFlag = TimelineHelper.MouseFlags.StartingBarMarkerPressed;
                    _initialStart = viewModel.Scene.Start; // 初期値を保存
                    _initialDuration = viewModel.Scene.Duration;
                }
                else
                {
                    _mouseFlag = TimelineHelper.MouseFlags.SeekBarPressed;
                    viewModel.CurrentTime.Value = pointerPt.Position.X
                        .PixelToTimeSpan(viewModel.Options.Value.Scale)
                        .RoundToRate(viewModel.Scene.FindHierarchicalParent<Project>() is { } proj
                            ? proj.GetFrameRate()
                            : 30);
                }

                e.Handled = true;
            }
        }
    }

    private void ZoomClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem menuItem
            && DataContext is GraphEditorViewModel viewModel)
        {
            float zoom;
            switch (menuItem.CommandParameter)
            {
                case string str:
                    if (!float.TryParse(str, out zoom))
                    {
                        return;
                    }

                    break;
                case double zoom1:
                    zoom = (float)zoom1;
                    break;
                case float zoom2:
                    zoom = zoom2;
                    break;
                default:
                    return;
            }

            viewModel.ScaleY.Value = zoom;
        }
    }

    private void OnGraphPanelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (KeyTimeMoveState != null
            && DataContext is GraphEditorViewModel { SelectedView.Value: { } selectedView } viewModel)
        {
            GraphEditorKeyFrameViewModel? itemViewModel = KeyTimeMoveState.KeyFrameViewModel;
            // ドラッグ中のキーフレームが左右のキーフレームを横断した場合、
            // Modelから再生成された、ViewModelを探す。
            // (横断すると、KeyFramesの順番を変える必要があるため)
            if (KeyTimeMoveState.Crossed)
            {
                double? y = KeyTimeMoveState.KeyFrameViewModel?.EndY.Value;
                itemViewModel = KeyTimeMoveState.KeyFrameViewModel =
                    selectedView.KeyFrames.FirstOrDefault(x => x.Model == KeyTimeMoveState.KeyFrame);

                if (y.HasValue && itemViewModel != null)
                {
                    int nextIndex = selectedView.KeyFrames.IndexOf(itemViewModel) + 1;
                    KeyTimeMoveState.NextKeyFrameViewModel = nextIndex < selectedView.KeyFrames.Count
                        ? selectedView.KeyFrames[nextIndex]
                        : null;

                    itemViewModel.EndY.Value = y.Value;
                }

                KeyTimeMoveState.Crossed = false;
            }

            if (itemViewModel != null)
            {
                PointerPoint point = e.GetCurrentPoint(grid);
                if (point.Properties.IsLeftButtonPressed)
                {
                    Point position = point.Position;
                    Point delta = position - KeyTimeMoveState.DragStart;
                    KeyTimeMoveState.DragStart = position;
                    float scale = viewModel.Options.Value.Scale;
                    int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;

                    if (KeyTimeMoveState.FollowingKeyFrames != null)
                    {
                        foreach (GraphEditorKeyFrameViewModel item in KeyTimeMoveState.FollowingKeyFrames.Append(
                                     itemViewModel))
                        {
                            double right = item.Right.Value + delta.X;
                            var timeSpan = right.PixelToTimeSpan(scale);
                            item.Right.Value = timeSpan.TimeToPixel(scale);
                        }
                    }
                    else
                    {
                        itemViewModel.EndY.Value -= delta.Y;
                        double right = itemViewModel.Right.Value + delta.X;
                        var timeSpan = right.PixelToTimeSpan(scale);
                        // 左のキーフレームに横断した場合
                        if (itemViewModel._previous.Value is { Model.KeyTime: TimeSpan prevTime }
                            && prevTime > timeSpan.RoundToRate(rate))
                        {
                            int index = selectedView.KeyFrames.IndexOf(itemViewModel);
                            var viewControlPoints = KeyTimeMoveState.ViewControlPoints;
                            var nextViewControlPoints = KeyTimeMoveState.NextViewControlPoints;
                            if (0 <= index - 1)
                            {
                                // 編集中のキーフレームの前後のキーフレームが変わるので、コントロールポイントを保存しておく
                                var prev = selectedView.KeyFrames[index - 1];
                                if (KeyTimeMoveState.NextViewControlPoints.HasValue)
                                {
                                    KeyTimeMoveState.NextViewControlPoints = (
                                        KeyTimeMoveState.NextViewControlPoints.Value.ControlPoint1,
                                        prev.RightTop.Value - prev.ControlPoint2.Value);
                                }

                                if (KeyTimeMoveState.ViewControlPoints.HasValue)
                                {
                                    KeyTimeMoveState.ViewControlPoints = (
                                        prev.LeftBottom.Value - prev.ControlPoint1.Value,
                                        KeyTimeMoveState.ViewControlPoints.Value.ControlPoint2);
                                }
                            }

                            itemViewModel.UpdateKeyTime(timeSpan);

                            // 現在編集中のキーフレームが横断したので、indexは現在の後になっている
                            if (index + 1 < selectedView.KeyFrames.Count && viewControlPoints.HasValue &&
                                nextViewControlPoints.HasValue)
                            {
                                // 編集中のキーフレームの前後のキーフレームが変わったので、移動前の時点でのコントロールポイントを表示上の位置で設定する
                                var nextNext = selectedView.KeyFrames[index + 1];
                                nextNext.UpdateControlPoint1(
                                    nextNext.LeftBottom.Value - viewControlPoints.Value.ControlPoint1);
                                nextNext.UpdateControlPoint2(
                                    nextNext.RightTop.Value - nextViewControlPoints.Value.ControlPoint2);
                            }

                            KeyTimeMoveState.Crossed = true;
                            e.Handled = true;
                            return;
                        }
                        else
                        {
                            timeSpan = new TimeSpan(Math.Max(0, timeSpan.Ticks));
                        }

                        // 右のキーフレームに横断した場合
                        if (itemViewModel._next is { Model.KeyTime: TimeSpan nextTime }
                            && timeSpan.RoundToRate(rate) > nextTime)
                        {
                            int index = selectedView.KeyFrames.IndexOf(itemViewModel);
                            var viewControlPoints = KeyTimeMoveState.ViewControlPoints;
                            var nextViewControlPoints = KeyTimeMoveState.NextViewControlPoints;
                            if (index + 2 < selectedView.KeyFrames.Count)
                            {
                                var nextNext = selectedView.KeyFrames[index + 2];
                                if (KeyTimeMoveState.NextViewControlPoints.HasValue)
                                {
                                    KeyTimeMoveState.NextViewControlPoints = (
                                        KeyTimeMoveState.NextViewControlPoints.Value.ControlPoint1,
                                        nextNext.RightTop.Value - nextNext.ControlPoint2.Value);
                                }

                                if (KeyTimeMoveState.ViewControlPoints.HasValue)
                                {
                                    KeyTimeMoveState.ViewControlPoints = (
                                        nextNext.LeftBottom.Value - nextNext.ControlPoint1.Value,
                                        KeyTimeMoveState.ViewControlPoints.Value.ControlPoint2);
                                }
                            }

                            itemViewModel.UpdateKeyTime(timeSpan);

                            // 現在編集中のキーフレームが横断したので、indexは現在の手前になっている
                            if (index < selectedView.KeyFrames.Count && viewControlPoints.HasValue &&
                                nextViewControlPoints.HasValue)
                            {
                                // 編集中のキーフレームの前後のキーフレームが変わったので、移動前の時点でのコントロールポイントを表示上の位置で設定する
                                var prev = selectedView.KeyFrames[index];
                                prev.UpdateControlPoint1(
                                    prev.LeftBottom.Value - viewControlPoints.Value.ControlPoint1);
                                prev.UpdateControlPoint2(
                                    prev.RightTop.Value - nextViewControlPoints.Value.ControlPoint2);
                            }

                            KeyTimeMoveState.Crossed = true;
                            e.Handled = true;
                            return;
                        }
                        else
                        {
                            timeSpan = new TimeSpan(Math.Max(0, timeSpan.Ticks));
                        }

                        itemViewModel.Right.Value = timeSpan.TimeToPixel(scale);
                    }

                    // SplineEasingは0-1の値なので、KeyTimeの移動中に伸縮してしまう。なので、ControlPointもKeyTimeの移動に合わせて移動するようにする。
                    if (KeyTimeMoveState.ViewControlPoints != null)
                    {
                        itemViewModel.UpdateControlPoint1(
                            itemViewModel.LeftBottom.Value - KeyTimeMoveState.ViewControlPoints.Value.ControlPoint1);
                        itemViewModel.UpdateControlPoint2(
                            itemViewModel.RightTop.Value - KeyTimeMoveState.ViewControlPoints.Value.ControlPoint2);
                    }

                    if (KeyTimeMoveState.NextKeyFrameViewModel != null &&
                        KeyTimeMoveState.NextViewControlPoints != null)
                    {
                        KeyTimeMoveState.NextKeyFrameViewModel.UpdateControlPoint1(
                            KeyTimeMoveState.NextKeyFrameViewModel.LeftBottom.Value -
                            KeyTimeMoveState.NextViewControlPoints.Value.ControlPoint1);
                        KeyTimeMoveState.NextKeyFrameViewModel.UpdateControlPoint2(
                            KeyTimeMoveState.NextKeyFrameViewModel.RightTop.Value -
                            KeyTimeMoveState.NextViewControlPoints.Value.ControlPoint2);
                    }

                    // マウスがスクロール外に行った時、スクロールを移動する
                    var scrollPos = e.GetPosition(scroll);
                    if (scrollPos.X < 0 || scrollPos.X > scroll.Bounds.Width)
                    {
                        scroll.Offset = new Vector(scroll.Offset.X + delta.X, scroll.Offset.Y);
                    }

                    if (scrollPos.Y < 0 || scrollPos.Y > scroll.Bounds.Height)
                    {
                        scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + delta.Y);
                    }

                    e.Handled = true;
                }
            }
        }
    }

    private void OnGraphPanelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel
            && KeyTimeMoveState != null
            && KeyTimeMoveState.KeyFrameViewModel != null)
        {
            if (KeyTimeMoveState.FollowingKeyFrames != null)
            {
                foreach (GraphEditorKeyFrameViewModel keyframe in KeyTimeMoveState.FollowingKeyFrames)
                {
                    keyframe.UpdateKeyTimeAndValue();
                }

                KeyTimeMoveState.KeyFrameViewModel.UpdateKeyTimeAndValue();
                viewModel.HistoryManager.Commit(CommandNames.MoveKeyFrame);
            }
            else
            {
                KeyTimeMoveState.KeyFrameViewModel.CommitKeyTimeAndValue();
            }

            KeyTimeMoveState = null;
            viewModel.EndEditting();
        }
    }

    private void SelectedView_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel
            && e.Source is MenuItem { DataContext: GraphEditorViewViewModel itemViewModel })
        {
            viewModel.SelectedView.Value = itemViewModel;
        }
    }

    private void ShowBpmGridFlyout(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphEditorViewModel viewModel) return;

        var bpmGrid = viewModel.Options.Value.BpmGrid;
        var flyout = new Editor.Components.Views.BpmGridFlyout
        {
            IsEnabledChecked = bpmGrid.IsEnabled,
            Bpm = (decimal)bpmGrid.Bpm,
            Subdivisions = bpmGrid.Subdivisions,
            OffsetSeconds = (decimal)bpmGrid.Offset.TotalSeconds,
        };
        flyout.OptionsChanged += (_, options) =>
        {
            viewModel.Options.Value = viewModel.Options.Value with { BpmGrid = options };
        };

        flyout.ShowAt(graphPanel, true);
    }
}
