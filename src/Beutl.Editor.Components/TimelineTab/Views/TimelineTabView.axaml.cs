using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Logging;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;
using MouseFlags = Beutl.Editor.Components.Helpers.TimelineHelper.MouseFlags;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineTabView : UserControl
{
    internal MouseFlags _mouseFlag = MouseFlags.Free;
    internal MediaTime _pointerFrame;
    private readonly TimelineRulerDrag _rulerDrag;
    private bool _rightButtonPressed;
    private SceneMarker? _pressedMarker;
    private Point _markerPressPosition;
    private DragSession? _markerDrag;
    private const double MarkerDragThreshold = 4d;
    private static readonly MediaTime s_defaultElementLength = MediaTime.FromSeconds(5);
    private readonly ILogger _logger = Log.CreateLogger<TimelineTabView>();
    private readonly CompositeDisposable _disposables = [];
    private readonly Func<PointerWheelEventArgs, bool> _usesGestureAxes;
    private readonly Func<TopLevel, IDisposable?> _attachNativeInput;
    private readonly Action<Visual, ulong> _alignmentFeedback;
    private TopLevel? _nativeInputRoot;
    private IDisposable? _nativeInputRegistration;
    private CancellationTokenSource? _scrollCts;
    private Task _scrollTask = Task.CompletedTask;
    private readonly Dictionary<Element, (ElementView Clip, ElementScopeView Scope)> _elementViews = [];
    private bool _elementViewsUpdatePending;

    public TimelineTabView()
        : this(NativeScrollInput.UsesGestureAxes)
    {
    }

    internal TimelineTabView(Func<PointerWheelEventArgs, bool> usesGestureAxes,
        Func<TopLevel, IDisposable?>? attachNativeInput = null, Action<Visual, ulong>? alignmentFeedback = null)
    {
        _usesGestureAxes = usesGestureAxes;
        _attachNativeInput = attachNativeInput ?? NativeScrollInput.Attach;
        _alignmentFeedback = alignmentFeedback ?? NativeHapticFeedback.PerformAlignment;
        _rulerDrag = new TimelineRulerDrag(() => _mouseFlag = MouseFlags.Free);
        InitializeComponent();
        TimelineEditingModeMenu.Populate(EditingModeMenu, "");

        gridSplitter.DragDelta += GridSplitter_DragDelta;

        RulerBar.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);
        ContentScroll.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);
        TimelinePanel.AddHandler(PointerMovedEvent, (_, e) =>
        {
            // Drag previews move view-model geometry before the scene is committed.
            if (e.GetCurrentPoint(TimelinePanel).Properties.IsLeftButtonPressed) QueueElementViewsUpdate();
        }, RoutingStrategies.Tunnel);

        TimelinePanel.AddHandler(DragDrop.DragOverEvent, TimelinePanel_DragOver);
        TimelinePanel.AddHandler(DragDrop.DropEvent, TimelinePanel_Drop);
        DragDrop.SetAllowDrop(TimelinePanel, true);

        this.SubscribeDataContextChange<TimelineTabViewModel>(OnDataContextAttached, OnDataContextDetached);

        PopulateAddElementSubMenu();

        if (TimelinePanel.ContextFlyout is FAMenuFlyout contextFlyout)
        {
            contextFlyout.Opening += (_, _) => PopulateAddFromTemplateSubMenu();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DetachNativeInput();
        if (TopLevel.GetTopLevel(this) is { } root)
        {
            _nativeInputRegistration = _attachNativeInput(root);
            _nativeInputRoot = root;
            root.Closed += OnNativeInputRootClosed;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachNativeInput();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnNativeInputRootClosed(object? sender, EventArgs e) => DetachNativeInput();

    private void DetachNativeInput()
    {
        if (_nativeInputRoot != null) _nativeInputRoot.Closed -= OnNativeInputRootClosed;
        _nativeInputRoot = null;
        _nativeInputRegistration?.Dispose();
        _nativeInputRegistration = null;
    }

    private void OnDataContextDetached(TimelineTabViewModel obj)
    {
        _scrollCts?.Cancel();
        ViewModel = null;
        ClearElementViews();
        TimelinePanel.Children.RemoveRange(2, TimelinePanel.Children.Count - 2);

        _disposables.Clear();
    }

    private void OnDataContextAttached(TimelineTabViewModel vm)
    {
        ViewModel = vm;

        TimelinePanel.Children.AddRange(vm.Inlines.Select(e => new InlineAnimationLayer { DataContext = e }));

        vm.Elements.TrackCollectionChanged(
                AddElement,
                RemoveElement,
                ClearElementViews)
            .DisposeWith(_disposables);

        // Batch additions and range changes share one viewport pass. Off-screen clips retain their
        // view models, but do not allocate a control tree, menus, or thumbnail reads until needed.
        Observable.FromEventPattern(h => vm.Scene.Edited += h, h => vm.Scene.Edited -= h)
            .Subscribe(_ => QueueElementViewsUpdate()).DisposeWith(_disposables);
        vm.Scale.Subscribe(_ => QueueElementViewsUpdate()).DisposeWith(_disposables);
        vm.LayerHeightChanged.Subscribe(_ => QueueElementViewsUpdate()).DisposeWith(_disposables);
        ContentScroll.GetObservable(ScrollViewer.OffsetProperty)
            .Subscribe(_ => QueueElementViewsUpdate()).DisposeWith(_disposables);
        ContentScroll.GetObservable(ScrollViewer.ViewportProperty)
            .Subscribe(_ => QueueElementViewsUpdate()).DisposeWith(_disposables);

        vm.Inlines.TrackCollectionChanged(
                OnAddedInline,
                OnRemovedInline,
                () => { })
            .DisposeWith(_disposables);

        ViewModel.ScrollTo.Subscribe(v => _scrollTask = ScrollTimelinePosition(v.Range, v.ZIndex))
            .DisposeWith(_disposables);

        // Scrolling realizes distant clips. Wait for it before looking up the target's control.
        ViewModel.HighlightElement
            .Subscribe(async element =>
            {
                Task scrolling = _scrollTask;
                await scrolling;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ViewModel != vm || _scrollTask != scrolling) return;
                    UpdateElementViews();
                    if (FindElementView(element) is { } view) EditFlash.Run(view.border);
                }, DispatcherPriority.Background);
            })
            .DisposeWith(_disposables);

        vm.CurrentTime
            .ObserveOnUIDispatcher()
            .Subscribe(OnCurrentTimeChangedForAutoScroll)
            .DisposeWith(_disposables);

        vm.IsRazorMode
            .ObserveOnUIDispatcher()
            .Subscribe(isRazor =>
            {
                Cursor cursor = isRazor ? Cursors.Cross : Cursors.Arrow;
                TimelinePanel.Cursor = cursor;
            })
            .DisposeWith(_disposables);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (ViewModel == null)
        {
            _logger.LogWarning("Timeline loaded without ViewModel.");
            return;
        }

        ViewModel.Options.Subscribe(options =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Vector2 offset = options.Offset;
                    ContentScroll.Offset = new(offset.X, offset.Y);
                    PaneScroll.Offset = new(0, offset.Y);
                }, DispatcherPriority.MaxValue);
            })
            .DisposeWith(_disposables);

        ContentScroll.ScrollChanged += ContentScroll_ScrollChanged;
    }

    internal TimelineTabViewModel? ViewModel { get; private set; }

    private void GridSplitter_DragDelta(object? sender, VectorEventArgs e)
    {
        ColumnDefinition def = grid.ColumnDefinitions[0];
        double last = def.ActualWidth + e.Vector.X;

        if (last is < 395 and > 385)
        {
            def.MaxWidth = 390;
            def.MinWidth = 390;
        }
        else
        {
            def.MaxWidth = double.PositiveInfinity;
            def.MinWidth = 200;
        }
    }

    private void AddElement(int index, ElementViewModel viewModel)
    {
        QueueElementViewsUpdate();
    }

    private void RemoveElement(int index, ElementViewModel viewModel)
        => RemoveElementViews(viewModel.Model);

    private void RemoveElementViews(Element element)
    {
        if (!_elementViews.Remove(element, out var views)) return;
        TimelinePanel.Children.Remove(views.Clip);
        TimelinePanel.Children.Remove(views.Scope);
        views.Clip.DataContext = null;
        views.Scope.DataContext = null;
    }

    private void ClearElementViews()
    {
        foreach (Element element in _elementViews.Keys.ToArray()) RemoveElementViews(element);
    }

    internal void QueueElementViewsUpdate()
    {
        if (_elementViewsUpdatePending) return;
        _elementViewsUpdatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _elementViewsUpdatePending = false;
            UpdateElementViews();
        }, DispatcherPriority.Render);
    }

    private void UpdateElementViews()
    {
        if (ViewModel is not { } timeline || ContentScroll.Viewport.Width <= 0) return;
        Size viewport = ContentScroll.Viewport;
        Avalonia.Vector offset = ContentScroll.Offset;
        // Keep a screen of overscan on each side for scrolling and edge drags.
        double left = offset.X - viewport.Width, right = offset.X + viewport.Width * 2;
        double top = offset.Y - viewport.Height, bottom = offset.Y + viewport.Height * 2;
        foreach (ElementViewModel element in timeline.Elements)
        {
            _elementViews.TryGetValue(element.Model, out var existing);
            bool visible = element.BorderMargin.Value.Left <= right
                && element.BorderMargin.Value.Left + element.Width.Value >= left
                && element.Margin.Value.Top <= bottom
                && element.Margin.Value.Top + Math.Max(element.Scope.Height.Value, FrameNumberHelper.LayerHeight) >= top;
            // A selected or focused view can own a drag, rename, or menu even outside the viewport.
            if (!visible && existing.Clip != null
                && (element.IsSelected.Value || existing.Clip.HasActiveInteraction)) continue;
            if (visible && existing.Clip == null)
            {
                var clip = new ElementView(_alignmentFeedback) { DataContext = element };
                var scope = new ElementScopeView { DataContext = element.Scope };
                _elementViews.Add(element.Model, (clip, scope));
                TimelinePanel.Children.Add(clip);
                TimelinePanel.Children.Add(scope);
            }
            else if (!visible) RemoveElementViews(element.Model);
        }
    }

    private void OnAddedInline(InlineAnimationLayerViewModel viewModel)
    {
        var view = new InlineAnimationLayer { DataContext = viewModel };

        TimelinePanel.Children.Add(view);
    }

    private void OnRemovedInline(InlineAnimationLayerViewModel viewModel)
    {
        IAnimatablePropertyAdapter prop = viewModel.Property;
        for (int i = 0; i < TimelinePanel.Children.Count; i++)
        {
            Control item = TimelinePanel.Children[i];
            if (item.DataContext is InlineAnimationLayerViewModel vm && vm.Property == prop)
            {
                TimelinePanel.Children.RemoveAt(i);
                break;
            }
        }
    }

    private ElementView? FindElementView(Element element)
    {
        return _elementViews.TryGetValue(element, out var views) ? views.Clip : null;
    }
}
