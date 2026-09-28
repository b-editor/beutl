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
    private int _contextVersion;

    public GraphEditorView()
    {
        InitializeComponent();
        RulerBar.PointerMoved += OnContentPointerMoved;
        RulerBar.PointerReleased += OnContentPointerReleased;
        RulerBar.PointerPressed += OnContentPointerPressed;
        graphPanel.PointerPressed += OnPlotPointerPressed;
        graphPanel.PointerMoved += OnContentPointerMoved;
        graphPanel.PointerCaptureLost += OnPlotPointerCaptureLost;
        graphPanel.AddHandler(PointerPressedEvent, OnPlotNavigationPressed, RoutingStrategies.Tunnel);
        KeyDown += OnGraphKeyDown;
        KeyUp += OnGraphKeyUp;
        LostFocus += (_, _) => _spacePressed = false;
        graphPanel.PointerMoved += OnGraphPanelPointerMoved;
        graphPanel.PointerReleased += OnGraphPanelPointerReleased;

        RulerBar.AddHandler(PointerWheelChangedEvent, OnHorizontalScalePointerWheelChanged, RoutingStrategies.Tunnel);
        graphPanel.AddHandler(PointerWheelChangedEvent, OnContentPointerWheelChanged, RoutingStrategies.Tunnel);
        verticalScale.AddHandler(PointerWheelChangedEvent, OnVerticalScalePointerWheelChanged,
            RoutingStrategies.Tunnel);
        scroll.ScrollChanged += OnScrollChanged;

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
        _contextVersion++;
        obj.SetClipboardViewContext(null);
        VelocityFlyout?.Hide();
        FinishInteraction(obj, cancel: true);
        _disposables.Clear();
    }

    private void OnDataContextAttached(GraphEditorViewModel obj)
    {
        obj.SetClipboardViewContext(() => ReferenceEquals(DataContext, obj) && IsEffectivelyVisible
            && TopLevel.GetTopLevel(this)?.PlatformImpl != null);
        AttachGraphInteractions(obj);

        obj.MinHeight
            .CombineLatest(scroll.GetObservable(BoundsProperty))
            .ObserveOnUIDispatcher()
            .Subscribe(v => graphPanel.Height = Math.Max(v.First, v.Second.Height))
            .DisposeWith(_disposables);

        obj.CurrentTime
            .ObserveOnUIDispatcher()
            .Subscribe(time => OnCurrentTimeChangedForAutoScroll(obj, time))
            .DisposeWith(_disposables);

        // A selected animation can change without loading the view again.
        obj.ScrollOffset.Subscribe(offset => scroll.Offset = offset)
            .DisposeWith(_disposables);
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
                if (_seekClickStart is { } start)
                {
                    Point delta = e.GetPosition(grid) - start;
                    if (Math.Abs(delta.X) + Math.Abs(delta.Y) >= 3)
                        _seekClickStart = null;
                }
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
            e.Pointer.Capture(null);
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

                e.Pointer.Capture(RulerBar);
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
