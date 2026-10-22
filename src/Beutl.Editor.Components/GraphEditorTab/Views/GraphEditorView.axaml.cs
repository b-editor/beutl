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
using FluentAvalonia.UI.Controls;
using Reactive.Bindings.Extensions;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;
using Vector = Avalonia.Vector;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private TimelineHelper.MouseFlags _mouseFlag = TimelineHelper.MouseFlags.Free;
    private readonly TimelineRulerDrag _rulerDrag;
    private MediaTime _pointerFrame;
    private int _contextVersion;

    public GraphEditorView()
    {
        InitializeComponent();
        _rulerDrag = new TimelineRulerDrag(() => _mouseFlag = TimelineHelper.MouseFlags.Free);
        KeyModifiers commandModifier = KeyGestureHelper.GetCommandModifier();
        foreach (FAMenuFlyoutItem item in ((FAMenuFlyout)graphPanel.ContextFlyout!).Items.OfType<FAMenuFlyoutItem>())
        {
            item.InputGesture = item.Tag switch
            {
                "Ease" => new KeyGesture(Key.F9),
                "EaseIn" => new KeyGesture(Key.F9, KeyModifiers.Shift),
                "EaseOut" => new KeyGesture(Key.F9, commandModifier),
                "Velocity" => new KeyGesture(Key.K, commandModifier | KeyModifiers.Shift),
                "DistributeEvenly" => new KeyGesture(Key.D, KeyModifiers.Alt),
                "Reverse" => new KeyGesture(Key.R, KeyModifiers.Alt),
                _ => item.InputGesture
            };
        }

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
        ValueScaleFlyout?.Hide();
        FinishInteraction(obj, cancel: true);
        graphPanel.ContextFlyout?.Hide();
        _disposables.Clear();
        SelectedViewMenu.Items.Clear();
    }

    private void OnDataContextAttached(GraphEditorViewModel obj)
    {
        obj.SetClipboardViewContext(() => ReferenceEquals(DataContext, obj) && IsEffectivelyVisible
            && TopLevel.GetTopLevel(this)?.PlatformImpl != null);
        AttachGraphInteractions(obj);

        foreach (GraphEditorViewViewModel view in obj.Views)
        {
            var item = new FARadioMenuFlyoutItem { Text = view.DisplayName, DataContext = view };
            item.Bind(FARadioMenuFlyoutItem.IsCheckedProperty, view.IsSelected)
                .DisposeWith(_disposables);
            item.Click += SelectedView_Click;
            UsageTracking.SetFeature(item, "GraphEditor.SelectedView");
            SelectedViewMenu.Items.Add(item);
        }

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

    private void OnCurrentTimeChangedForAutoScroll(GraphEditorViewModel viewModel, MediaTime currentTime)
    {
        TimelineHelper.AutoScrollToPlayhead(scroll, viewModel.EditorContext, viewModel.Options, currentTime);
    }

    private void OnContentPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel)
        {
            PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);
            viewModel.UpdatePointerPosition(pointerPt.Position.X);
            Rational rate = SceneTimeRangeService.GetFrameRate(viewModel.Scene);
            _pointerFrame = pointerPt.Position.X.PixelToTime(viewModel.Options.Value.Scale).RoundToFrame(rate, MidpointRounding.AwayFromZero);

            if (_pointerFrame < MediaTime.Zero)
            {
                _pointerFrame = MediaTime.Zero;
            }

            if (_mouseFlag == TimelineHelper.MouseFlags.SeekBarPressed)
            {
                if (_seekClickStart is { } start)
                {
                    Point delta = e.GetPosition(grid) - start;
                    if (Math.Abs(delta.X) + Math.Abs(delta.Y) >= 3)
                        _seekClickStart = null;
                }
                viewModel.Seek(_pointerFrame);
                e.Handled = true;
            }
            else if (_rulerDrag.Move(_mouseFlag, viewModel.EditorContext, RulerBar, viewModel.Scene, _pointerFrame))
            {
                e.Handled = true;
            }
            else
            {
                UpdateScaleCursor(viewModel, e, pointerPt);
            }
        }
    }

    private void UpdateScaleCursor(GraphEditorViewModel viewModel, PointerEventArgs e, PointerPoint pointerPt)
    {
        Point posScale = e.GetPosition(scale);
        double startingBarX = viewModel.StartingBarMargin.Value.Left;
        double endingBarX = viewModel.EndingBarMargin.Value.Left;

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

    private void OnContentPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not GraphEditorViewModel) return;
        PointerPoint pointerPt = e.GetCurrentPoint(graphPanel);

        if (pointerPt.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            _rulerDrag.Release();

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

                _mouseFlag = _rulerDrag.Press(
                    viewModel.Scene, pointerPt.Position.X, scalePoint.Y, startingBarX, endingBarX);
                if (_mouseFlag == TimelineHelper.MouseFlags.SeekBarPressed)
                {
                    viewModel.Seek(pointerPt.Position.X
                        .PixelToTime(viewModel.Options.Value.Scale)
                        .RoundToFrame(SceneTimeRangeService.GetFrameRate(viewModel.Scene), MidpointRounding.AwayFromZero));
                }

                e.Pointer.Capture(RulerBar);
                e.Handled = true;
            }
        }
    }

    private void ZoomClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is FAMenuFlyoutItem menuItem
            && DataContext is GraphEditorViewModel viewModel
            && TimelineHelper.TryParseZoom(menuItem.CommandParameter, out float zoom))
        {
            viewModel.ScaleY.Value = zoom;
        }
    }

    private void SelectedView_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel viewModel
            && e.Source is FAMenuFlyoutItem { DataContext: GraphEditorViewViewModel itemViewModel })
        {
            viewModel.SelectedView.Value = itemViewModel;
        }
    }

    private void ShowBpmGridFlyout(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphEditorViewModel viewModel) return;

        TimelineHelper.ShowBpmGridFlyout(viewModel.Options, graphPanel);
    }
}
