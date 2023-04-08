using System.Numerics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Animation;
using Beutl.ProjectSystem;
using Beutl.Utilities;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings.Extensions;

using Path = Avalonia.Controls.Shapes.Path;

namespace Beutl.Views;

public partial class GraphEditorView : UserControl
{
    private bool _pressed;
    private TimeSpan _pointerFrame;
    private CompositeDisposable _disposables = new();

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

        scale.AddHandler(PointerWheelChangedEvent, OnContentPointerWheelChanged, RoutingStrategies.Tunnel);
        graphPanel.AddHandler(PointerWheelChangedEvent, OnContentPointerWheelChanged, RoutingStrategies.Tunnel);

        this.SubscribeDataContextChange<GraphEditorViewModel>(
            OnDataContextAttached,
            OnDataContextDetached);

        views.ItemContainerGenerator.Materialized += OnViewMaterialized;
        views.ItemContainerGenerator.Dematerialized += OnViewDematerialized;
    }

    private void OnViewMaterialized(object? sender, ItemContainerEventArgs e)
    {
        foreach (ItemContainerInfo item in e.Containers)
        {
            if (item.Item is GraphEditorViewViewModel viewModel
                && item.ContainerControl is ContentPresenter container)
            {
                container.Bind(ZIndexProperty, viewModel.IsSelected.Select(v => v ? 1 : 0));
            }
        }
    }

    private void OnViewDematerialized(object? sender, ItemContainerEventArgs e)
    {
        foreach (ItemContainerInfo item in e.Containers)
        {
            if (item.ContainerControl is ContentPresenter container)
            {
                container.Bind(ZIndexProperty, Observable.Return(BindingValue<int>.Unset));
            }
        }
    }

    private void OnDataContextDetached(GraphEditorViewModel obj)
    {
        _disposables.Clear();
    }

    private void OnDataContextAttached(GraphEditorViewModel obj)
    {
        obj.ScrollOffset.Subscribe(offset => scroll.Offset = offset)
            .DisposeWith(_disposables);
        scroll.GetObservable(ScrollViewer.OffsetProperty)
            .Subscribe(offset => obj.ScrollOffset.Value = offset)
            .DisposeWith(_disposables);

        obj.MinHeight
            .CombineLatest(scroll.GetObservable(BoundsProperty))
            .ObserveOnUIDispatcher()
            .Subscribe(v => graphPanel.Height = Math.Max(v.First, v.Second.Height))
            .DisposeWith(_disposables);

        obj.SelectedView
            .CombineWithPrevious()
            .Subscribe(t =>
            {

            })
            .DisposeWith(_disposables);
    }

    private static float CoerceScaleX(float value)
    {
        if (MathUtilities.AreClose(value, 1))
            value = 1F;
        else if (MathUtilities.AreClose(value, 2))
            value = 2F;
        else if (MathUtilities.AreClose(value, 0.75))
            value = 0.75F;
        else if (MathUtilities.AreClose(value, 0.50))
            value = 0.50F;
        else if (MathUtilities.AreClose(value, 0.25))
            value = 0.25F;

        return Math.Min(value, 2);
    }

    private void OnContentPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            Avalonia.Vector aOffset = scroll.Offset;
            float scale = viewModel.Options.Value.Scale;
            var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);

            if (e.KeyModifiers == KeyModifiers.Control)
            {
                // 目盛りのスケールを変更
                float oldScale = viewModel.Options.Value.Scale;
                TimeSpan ts = offset.X.ToTimeSpanF(oldScale);
                float deltaScale = (float)(e.Delta.Y / 10) * oldScale;
                scale = CoerceScaleX(deltaScale + oldScale);

                offset.X = ts.ToPixelF(scale);
            }
            else if (e.KeyModifiers.HasAllFlags(KeyModifiers.Control | KeyModifiers.Shift))
            {
                double oldScale = viewModel.ScaleY.Value;
                double scaleY = oldScale + (e.Delta.Y / 100);
                scaleY = Math.Clamp(scaleY, 0.01, 8.75);

                //offset.Y *= scaleY;
                viewModel.ScaleY.Value = scaleY;
            }
            else if (e.KeyModifiers == KeyModifiers.Shift)
            {
                // オフセット(X) をスクロール
                offset.X -= (float)(e.Delta.X * 50);
            }
            else
            {
                // オフセット(Y) をスクロール
                offset.Y -= (float)(e.Delta.Y * 50);
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

            _pointerFrame = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale)
                .RoundToRate(viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30);

            if (_pressed)
            {
                viewModel.Scene.CurrentFrame = _pointerFrame;
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

                viewModel.Scene.CurrentFrame = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale)
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
            && sender is Path { DataContext: GraphEditorKeyFrameViewModel viewModel, Tag: string tag } shape)
        {
            Point position = new(e.GetPosition(views).X, e.GetPosition(grid).Y);
            position = position.WithX(Math.Clamp(position.X, viewModel.Left.Value, viewModel.Right.Value));
            Point delta = position - _cPointstart;
            _cPointstart = position;
            bool result = tag switch
            {
                "ControlPoint1" => viewModel.UpdateControlPoint1(viewModel.ControlPoint1.Value + delta),
                "ControlPoint2" => viewModel.UpdateControlPoint2(viewModel.ControlPoint2.Value + delta),
                _ => false,
            };

            if (!result)
            {
                _cPointPressed = false;
            }

            e.Handled = true;
        }
    }

    private void OnControlPointPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel
            && sender is Path
            {
                Tag: string tag,
                DataContext: GraphEditorKeyFrameViewModel
                {
                    Model.Easing: Animation.Easings.SplineEasing splineEasing
                }
            } shape)
        {
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
            && sender is Path { DataContext: GraphEditorKeyFrameViewModel itemViewModel, Tag: string tag })
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
}
