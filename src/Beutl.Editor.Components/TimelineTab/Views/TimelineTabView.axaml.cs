using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
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
    private TopLevel? _nativeInputRoot;
    private IDisposable? _nativeInputRegistration;
    private CancellationTokenSource? _scrollCts;
    private Task _scrollTask = Task.CompletedTask;

    public TimelineTabView()
        : this(NativeScrollInput.UsesGestureAxes)
    {
    }

    internal TimelineTabView(Func<PointerWheelEventArgs, bool> usesGestureAxes,
        Func<TopLevel, IDisposable?>? attachNativeInput = null)
    {
        _usesGestureAxes = usesGestureAxes;
        _attachNativeInput = attachNativeInput ?? NativeScrollInput.Attach;
        _rulerDrag = new TimelineRulerDrag(() => _mouseFlag = MouseFlags.Free);
        InitializeComponent();
        TimelineEditingModeMenu.Populate(EditingModeMenu, "");

        gridSplitter.DragDelta += GridSplitter_DragDelta;

        RulerBar.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);
        ContentScroll.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);
        ContentScroll.ScrollChanged += ContentScroll_ScrollChanged;
        TimelinePanel.AddHandler(PointerReleasedEvent, (_, _) => InvalidateElementViewport(), RoutingStrategies.Tunnel, true);
        TimelinePanel.AddHandler(PointerCaptureLostEvent, (_, _) => InvalidateElementViewport(), RoutingStrategies.Bubble, true);
        TimelinePanel.AddHandler(PointerMovedEvent, OnElementPointerMoved, RoutingStrategies.Tunnel, true);
        TimelinePanel.PointerExited += OnElementPointerExited;

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
        ViewModel = null;
        ClearElementViewport();
        _recycledElementViews.Clear();
        int contentStart = TimelinePanel.Children.IndexOf(overlay) + 1;
        TimelinePanel.Children.RemoveRange(contentStart, TimelinePanel.Children.Count - contentStart);

        _disposables.Clear();
    }

    private void OnDataContextAttached(TimelineTabViewModel vm)
    {
        ViewModel = vm;

        TimelinePanel.Children.AddRange(vm.Inlines.Select(e => new InlineAnimationLayer { DataContext = e }));

        vm.Elements.ForEachItem(
                AddElement,
                RemoveElement,
                ClearElementViewport)
            .DisposeWith(_disposables);

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
                    UpdateElementViewport();
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

        InvalidateElementViewport();
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
        ViewportElement? entry = _viewportElements.Find(item => ReferenceEquals(item.Model.Model, element));
        return entry is null ? null : entry.View ?? RealizeCompactElement(entry);
    }
}
