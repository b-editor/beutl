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
using Beutl.Services;
using Beutl.ViewModels;

using Reactive.Bindings.Extensions;

using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;

namespace Beutl.Views;

public partial class GraphEditorView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private bool _pressed;
    private TimeSpan _lastRightClickPoint;
    private TimeSpan _pointerFrame;

    public GraphEditorView()
    {
        InitializeComponent();
        scale.PointerExited += OnContentPointerExited;
        scale.PointerMoved += OnContentPointerMoved;
        scale.PointerReleased += OnContentPointerReleased;
        scale.PointerPressed += OnContentPointerPressed;
        background.PointerExited += OnContentPointerExited;
        background.PointerMoved += OnContentPointerMoved;
        background.PointerReleased += OnContentPointerReleased;
        background.PointerPressed += OnContentPointerPressed;
        graphPanel.PointerMoved += OnGraphPanelPointerMoved;
        graphPanel.PointerReleased += OnGraphPanelPointerReleased;

        scroll.PointerPressed += OnScrollPointerPressed;

        scale.AddHandler(PointerWheelChangedEvent, OnContentPointerWheelChanged, RoutingStrategies.Tunnel);
        graphPanel.AddHandler(PointerWheelChangedEvent, OnContentPointerWheelChanged, RoutingStrategies.Tunnel);

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
        if (e.Data.Get(KnownLibraryItemFormats.Easing) is Easing easing
            && DataContext is GraphEditorViewModel { Options.Value.Scale: { } scale } viewModel)
        {
            TimeSpan time = e.GetPosition(graphPanel).X.ToTimeSpan(scale);
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
            container.Bind(ZIndexProperty, Observable.Return(BindingValue<int>.Unset));
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
                edelta = edelta.SwapAxis();
            }

            if (e.KeyModifiers == KeyModifiers.Control)
            {
                // 目盛りのスケールを変更
                UpdateHorizontalZoom(e, ref scale, ref offset);
            }
            else if (e.KeyModifiers.HasAllFlags(KeyModifiers.Control | KeyModifiers.Shift))
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

    private void OnContentPointerExited(object? sender, PointerEventArgs e)
    {
        _pressed = false;
    }

    private void OnContentPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);

            int rate = viewModel.Scene.FindHierarchicalParent<Project>().GetFrameRate();
            _pointerFrame = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale).RoundToRate(rate);

            if (_pointerFrame >= viewModel.Scene.Duration)
            {
                _pointerFrame = viewModel.Scene.Duration - TimeSpan.FromSeconds(1d / rate);
            }
            if (_pointerFrame < TimeSpan.Zero)
            {
                _pointerFrame = TimeSpan.Zero;
            }

            if (_pressed)
            {
                viewModel.EditorContext.CurrentTime.Value = _pointerFrame;
                e.Handled = true;
            }
        }
    }

    private void OnContentPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);

        if (pointerPt.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            _pressed = false;
        }
    }

    private void OnContentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);

            if (pointerPt.Properties.IsLeftButtonPressed)
            {
                _pressed = true;

                viewModel.EditorContext.CurrentTime.Value = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale)
                    .RoundToRate(viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30);

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
    private (float, float) _oldValue;

    private void OnControlPointPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_cPointPressed
            && sender is Shape { DataContext: GraphEditorKeyFrameViewModel viewModel, Tag: string tag })
        {
            Point position = new(e.GetPosition(views).X, e.GetPosition(grid).Y);
            position = position.WithX(Math.Clamp(position.X, viewModel.Left.Value, viewModel.Right.Value));
            Point delta = position - _cPointstart;
            _cPointstart = position;
            switch (tag)
            {
                case "ControlPoint1":
                    viewModel.UpdateControlPoint1(viewModel.ControlPoint1.Value + delta);
                    break;
                case "ControlPoint2":
                    viewModel.UpdateControlPoint2(viewModel.ControlPoint2.Value + delta);
                    break;
            }

            e.Handled = true;
        }
    }

    private void OnControlPointPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel
            && sender is Shape
            {
                Tag: string tag,
                DataContext: GraphEditorKeyFrameViewModel
                {
                    Model.Easing: Animation.Easings.SplineEasing splineEasing
                }
            } shape)
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
                _oldValue = tag switch
                {
                    "ControlPoint1" => (splineEasing.X1, splineEasing.Y1),
                    "ControlPoint2" => (splineEasing.X2, splineEasing.Y2),
                    _ => default,
                };
                _cPointPressed = true;
                _cPointstart = new(e.GetPosition(views).X, point.Position.Y);
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
            switch (tag)
            {
                case "ControlPoint1":
                    itemViewModel.SubmitControlPoint1(_oldValue.Item1, _oldValue.Item2);
                    break;
                case "ControlPoint2":
                    itemViewModel.SubmitControlPoint2(_oldValue.Item1, _oldValue.Item2);
                    break;
            }

            viewModel.EndEditting();
            _cPointPressed = false;
            e.Handled = true;
        }
    }

    private bool _keyTimePressed;
    private Point _keyTimeStart;
    private IKeyFrame? _keyframe;
    private TimeSpan _oldKeyTime;
    private GraphEditorKeyFrameViewModel? _keyframeViewModel;
    private bool _crossed;

    private void OnGraphPanelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_keyTimePressed
            && DataContext is GraphEditorViewModel { SelectedView.Value: { } selectedView } viewModel
            && _keyframe != null)
        {
            GraphEditorKeyFrameViewModel? itemViewModel = _keyframeViewModel;
            if (_crossed)
            {
                double? y = _keyframeViewModel?.EndY.Value;
                itemViewModel = _keyframeViewModel = selectedView.KeyFrames.FirstOrDefault(x => x.Model == _keyframe);

                if (y.HasValue && itemViewModel != null)
                {
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

                    itemViewModel.EndY.Value -= delta.Y;

                    float scale = viewModel.Options.Value.Scale;
                    int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;

                    double right = itemViewModel.Right.Value + delta.X;
                    var timeSpan = right.ToTimeSpan(scale);
                    if (itemViewModel._previous.Value is { Model.KeyTime: TimeSpan prevTime }
                        && prevTime > timeSpan.RoundToRate(rate))
                    {
                        itemViewModel.SubmitCrossed(timeSpan);
                        _crossed = true;
                        e.Handled = true;
                        return;
                    }
                    else
                    {
                        timeSpan = new TimeSpan(Math.Max(0, timeSpan.Ticks));
                    }

                    if (itemViewModel._next is { Model.KeyTime: TimeSpan nextTime }
                        && timeSpan.RoundToRate(rate) > nextTime)
                    {
                        itemViewModel.SubmitCrossed(timeSpan);
                        _crossed = true;
                        e.Handled = true;
                        return;
                    }
                    else
                    {
                        timeSpan = new TimeSpan(Math.Max(0, timeSpan.Ticks));
                    }

                    itemViewModel.Right.Value = timeSpan.ToPixel(scale);

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
            _keyframeViewModel.SubmitKeyTimeAndValue(_oldKeyTime);
            _keyframe = null;
            _keyframeViewModel = null;
            _crossed = false;
            _keyTimePressed = false;
            viewModel.EndEditting();
        }
    }

    private void OnScrollPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);

            if (pointerPt.Properties.IsRightButtonPressed)
            {
                _lastRightClickPoint = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale)
                    .RoundToRate(viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30);
                TimeSpan localTime = viewModel.ConvertKeyTime(_lastRightClickPoint);

                deleteMenuItem.IsEnabled = viewModel.Animation.KeyFrames.Any(x => x.KeyTime == localTime);
            }
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
                _oldKeyTime = _keyframe.KeyTime;
                _crossed = false;
                viewModel.BeginEditing();
                e.Handled = true;
            }
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

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            viewModel.RemoveKeyFrame(_lastRightClickPoint);
        }
    }
}
