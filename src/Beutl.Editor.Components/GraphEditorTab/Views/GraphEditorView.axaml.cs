using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Configuration;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Reactive.Bindings.Extensions;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;
using Vector = Avalonia.Vector;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    internal TimelineHelper.MouseFlags _mouseFlag = TimelineHelper.MouseFlags.Free;
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

        DragDrop.SetAllowDrop(graphPanel, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrap);
    }

    private void OnDrap(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.Easing) is { } typeName
            && TypeFormat.ToType(typeName) is { } type
            && Activator.CreateInstance(type) is Easing easing
            && DataContext is GraphEditorViewModel { Options.Value.Scale: { } scale } viewModel)
        {
            TimeSpan time = e.GetPosition(graphPanel).X.PixelToTimeSpan(scale);
            viewModel.DropEasing(easing, time);
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(BeutlDataFormats.Easing))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

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
            if (_cPointPressed)
            {
                _cPointstart += new Point(0, delta);
            }
            else if (_keyTimePressed)
            {
                _keyTimeStart += new Point(0, delta);
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
            if (_cPointPressed)
            {
                _cPointstart += new Point(0, delta);
            }
            else if (_keyTimePressed)
            {
                _keyTimeStart += new Point(0, delta);
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
                if (TimelineHelper.IsPointInTimelineScaleMarker(pointerPt.Position.X, posScale.Y, startingBarX, endingBarX))
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

    // Behaviorに分ける
    // Todo: EaseLineをSplineEasingの時だけ、ViewModelのControlPointにバインドする
    private bool _cPointPressed;

    private Point _cPointstart;

    // 反対側のコントロールポイントのキーフレームを探す
    private GraphEditorKeyFrameViewModel? FindOppositeKeyFrame(GraphEditorKeyFrameViewModel item, string tag)
    {
        if (DataContext is not GraphEditorViewModel { SelectedView.Value: { } selectedView } viewModel) return null;
        int index = selectedView.KeyFrames.IndexOf(item);

        return tag switch
        {
            "ControlPoint1" => index == 0 ? null : selectedView.KeyFrames[index - 1],
            "ControlPoint2" => index == selectedView.KeyFrames.Count - 1 ? null : selectedView.KeyFrames[index + 1],
            _ => null
        };
    }

    private void OnControlPointPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_cPointPressed
            && DataContext is GraphEditorViewModel editorViewModel
            && sender is Shape { DataContext: GraphEditorKeyFrameViewModel viewModel, Tag: string tag })
        {
            Point position = new(e.GetPosition(views).X, e.GetPosition(grid).Y);
            Point delta = position - _cPointstart;
            Point d = default;
            switch (tag)
            {
                case "ControlPoint1":
                    viewModel.UpdateControlPoint1(viewModel.ControlPoint1.Value + delta);
                    d = viewModel.LeftBottom.Value - viewModel.ControlPoint1.Value;
                    break;
                case "ControlPoint2":
                    viewModel.UpdateControlPoint2(viewModel.ControlPoint2.Value + delta);
                    d = viewModel.RightTop.Value - viewModel.ControlPoint2.Value;
                    break;
            }

            position = position.WithX(Math.Clamp(position.X, viewModel.Left.Value, viewModel.Right.Value));
            _cPointstart = position;

            if (!editorViewModel.Separately.Value)
            {
                double radians = Math.Atan2(d.X, d.Y);
                radians -= MathF.PI / 2;

                var oppotite = FindOppositeKeyFrame(viewModel, tag);
                if (oppotite != null)
                {
                    static double Length(Point p)
                    {
                        return Math.Sqrt((p.X * p.X) + (p.Y * p.Y));
                    }

                    static Point CalculatePoint(double radians, double radius)
                    {
                        double x = Math.Cos(radians) * radius;
                        double y = Math.Sin(radians) * radius;
                        // Y座標は反転
                        return new Point(x, -y);
                    }

                    bool symmetry = editorViewModel.Symmetry.Value;
                    double length;
                    switch (tag)
                    {
                        case "ControlPoint2":
                            length = symmetry
                                ? Length(d)
                                : Length(oppotite.LeftBottom.Value - oppotite.ControlPoint1.Value);

                            oppotite.UpdateControlPoint1(oppotite.LeftBottom.Value + CalculatePoint(radians, length));
                            break;
                        case "ControlPoint1":
                            length = symmetry
                                ? Length(d)
                                : Length(oppotite.RightTop.Value - oppotite.ControlPoint2.Value);

                            oppotite.UpdateControlPoint2(oppotite.RightTop.Value + CalculatePoint(radians, length));
                            break;
                    }
                }
            }

            e.Handled = true;
        }
    }

    private void OnControlPointPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel
            && sender is Shape { DataContext: GraphEditorKeyFrameViewModel { Model.Easing: Animation.Easings.SplineEasing } } shape)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt)
                && shape.GetLogicalSiblings().OfType<Path>().FirstOrDefault(v => v.Name == "KeyTimeIcon") is Path ki
                && ki.InputHitTest(e.GetPosition(ki)) == ki)
            {
                ki.RaiseEvent(e);
                return;
            }

            PointerPoint point = e.GetCurrentPoint(grid);

            if (point.Properties.IsLeftButtonPressed)
            {
                _cPointPressed = true;
                _cPointstart = new Point(e.GetPosition(views).X, point.Position.Y);
                viewModel.BeginEditing();
                e.Handled = true;
            }
        }
    }

    private void OnControlPointPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel
            && _cPointPressed
            && sender is Shape { DataContext: GraphEditorKeyFrameViewModel itemViewModel, Tag: string tag })
        {
            viewModel.EndEditting();
            _cPointPressed = false;
            e.Handled = true;
        }
    }

    private bool _keyTimePressed;
    private Point _keyTimeStart;

    // ViewControlPoint2は後ろの位置からの相対的な位置
    // ドラッグ前のコントロールポイントの位置（表示上の点）
    private (Point ControlPoint1, Point ControlPoint2)? _viewControlPoints;
    private (Point ControlPoint1, Point ControlPoint2)? _nextViewControlPoints;

    private IKeyFrame? _keyframe;
    private GraphEditorKeyFrameViewModel? _keyframeViewModel;
    private GraphEditorKeyFrameViewModel? _nextKeyframeViewModel;

    private bool _crossed;

    // 追従移動するキーフレーム
    private GraphEditorKeyFrameViewModel[]? _followingKeyFrames;

    private void OnGraphPanelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_keyTimePressed
            && DataContext is GraphEditorViewModel { SelectedView.Value: { } selectedView } viewModel
            && _keyframe != null)
        {
            GraphEditorKeyFrameViewModel? itemViewModel = _keyframeViewModel;
            // ドラッグ中のキーフレームが左右のキーフレームを横断した場合、
            // Modelから再生成された、ViewModelを探す。
            // (横断すると、KeyFramesの順番を変える必要があるため)
            if (_crossed)
            {
                double? y = _keyframeViewModel?.EndY.Value;
                itemViewModel = _keyframeViewModel = selectedView.KeyFrames.FirstOrDefault(x => x.Model == _keyframe);

                if (y.HasValue && itemViewModel != null)
                {
                    int nextIndex = selectedView.KeyFrames.IndexOf(itemViewModel) + 1;
                    _nextKeyframeViewModel = nextIndex < selectedView.KeyFrames.Count
                        ? selectedView.KeyFrames[nextIndex]
                        : null;

                    itemViewModel.EndY.Value = y.Value;
                }

                _crossed = false;
            }

            if (itemViewModel != null)
            {
                PointerPoint point = e.GetCurrentPoint(grid);
                if (point.Properties.IsLeftButtonPressed)
                {
                    Point position = point.Position;
                    Point delta = position - _keyTimeStart;
                    _keyTimeStart = position;
                    float scale = viewModel.Options.Value.Scale;
                    int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;

                    if (_followingKeyFrames != null)
                    {
                        foreach (GraphEditorKeyFrameViewModel item in _followingKeyFrames.Append(itemViewModel))
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
                            var viewControlPoints = _viewControlPoints;
                            var nextViewControlPoints = _nextViewControlPoints;
                            if (0 <= index - 1)
                            {
                                // 編集中のキーフレームの前後のキーフレームが変わるので、コントロールポイントを保存しておく
                                var prev = selectedView.KeyFrames[index - 1];
                                if (_nextViewControlPoints.HasValue)
                                {
                                    _nextViewControlPoints = (
                                        _nextViewControlPoints.Value.ControlPoint1,
                                        prev.RightTop.Value - prev.ControlPoint2.Value);
                                }

                                if (_viewControlPoints.HasValue)
                                {
                                    _viewControlPoints = (
                                        prev.LeftBottom.Value - prev.ControlPoint1.Value,
                                        _viewControlPoints.Value.ControlPoint2);
                                }
                            }

                            itemViewModel.SubmitCrossed(timeSpan);

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

                            _crossed = true;
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
                            var viewControlPoints = _viewControlPoints;
                            var nextViewControlPoints = _nextViewControlPoints;
                            if (index + 2 < selectedView.KeyFrames.Count)
                            {
                                var nextNext = selectedView.KeyFrames[index + 2];
                                if (_nextViewControlPoints.HasValue)
                                {
                                    _nextViewControlPoints = (_nextViewControlPoints.Value.ControlPoint1,
                                        nextNext.RightTop.Value - nextNext.ControlPoint2.Value);
                                }

                                if (_viewControlPoints.HasValue)
                                {
                                    _viewControlPoints = (nextNext.LeftBottom.Value - nextNext.ControlPoint1.Value,
                                        _viewControlPoints.Value.ControlPoint2);
                                }
                            }

                            itemViewModel.SubmitCrossed(timeSpan);

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

                            _crossed = true;
                            e.Handled = true;
                            return;
                        }
                        else
                        {
                            timeSpan = new TimeSpan(Math.Max(0, timeSpan.Ticks));
                        }

                        itemViewModel.Right.Value = timeSpan.TimeToPixel(scale);
                    }

                    if (_viewControlPoints != null)
                    {
                        itemViewModel.UpdateControlPoint1(
                            itemViewModel.LeftBottom.Value - _viewControlPoints.Value.ControlPoint1);
                        itemViewModel.UpdateControlPoint2(
                            itemViewModel.RightTop.Value - _viewControlPoints.Value.ControlPoint2);
                    }

                    if (_nextKeyframeViewModel != null && _nextViewControlPoints != null)
                    {
                        _nextKeyframeViewModel.UpdateControlPoint1(
                            _nextKeyframeViewModel.LeftBottom.Value - _nextViewControlPoints.Value.ControlPoint1);
                        _nextKeyframeViewModel.UpdateControlPoint2(
                            _nextKeyframeViewModel.RightTop.Value - _nextViewControlPoints.Value.ControlPoint2);
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
            && _keyTimePressed
            && _keyframeViewModel != null)
        {
            if (_followingKeyFrames != null)
            {
                foreach (GraphEditorKeyFrameViewModel keyframe in _followingKeyFrames)
                {
                    keyframe.UpdateKeyTimeAndValue();
                }

                _keyframeViewModel.UpdateKeyTimeAndValue();
                viewModel.HistoryManager.Commit(CommandNames.MoveKeyFrame);
            }
            else
            {
                _keyframeViewModel.CommitKeyTimeAndValue();
            }

            _followingKeyFrames = null;
            _keyframe = null;
            _keyframeViewModel = null;
            _crossed = false;
            _keyTimePressed = false;
            viewModel.EndEditting();
        }
    }

    private void OnKeyTimePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel
            && sender is Path { DataContext: GraphEditorKeyFrameViewModel itemViewModel })
        {
            PointerPoint point = e.GetCurrentPoint(grid);

            if (point.Properties.IsLeftButtonPressed)
            {
                _keyTimeStart = point.Position;
                _keyTimePressed = true;
                _keyframe = itemViewModel.Model;
                _keyframeViewModel = itemViewModel;
                _viewControlPoints = GetSplineControlPoints(itemViewModel);
                int nextIndex = itemViewModel.Parent.KeyFrames.IndexOf(itemViewModel) + 1;
                _nextKeyframeViewModel = nextIndex < itemViewModel.Parent.KeyFrames.Count
                    ? itemViewModel.Parent.KeyFrames[nextIndex]
                    : null;
                _nextViewControlPoints = _nextKeyframeViewModel != null
                    ? GetSplineControlPoints(_nextKeyframeViewModel)
                    : null;

                _crossed = false;
                viewModel.BeginEditing();
                e.Handled = true;
                if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    _followingKeyFrames = itemViewModel.Parent.KeyFrames.Where(i => i != itemViewModel).ToArray();
                }
            }
        }
    }

    private (Point, Point)? GetSplineControlPoints(GraphEditorKeyFrameViewModel keyFrame)
    {
        if (keyFrame.Model.Easing is SplineEasing)
        {
            var viewControlPoint1 = keyFrame.ControlPoint1.Value;
            var viewControlPoint2 = keyFrame.ControlPoint2.Value;
            viewControlPoint1 = keyFrame.LeftBottom.Value - viewControlPoint1;
            viewControlPoint2 = keyFrame.RightTop.Value - viewControlPoint2;

            return (viewControlPoint1, viewControlPoint2);
        }
        else
        {
            return default;
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

    private void UseGlobalClock_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            viewModel.UpdateUseGlobalClock(!viewModel.UseGlobalClock.Value);
        }
    }
}
