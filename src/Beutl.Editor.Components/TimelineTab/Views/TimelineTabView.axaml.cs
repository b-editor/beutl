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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;
using MouseFlags = Beutl.Editor.Components.Helpers.TimelineHelper.MouseFlags;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineTabView : UserControl
{
    internal MouseFlags _mouseFlag = MouseFlags.Free;
    internal TimeSpan _pointerFrame;
    private TimeSpan _initialStart;
    private TimeSpan _initialDuration;
    private bool _rightButtonPressed;
    private SceneMarker? _pressedMarker;
    private TimeSpan _markerInitialTime;
    private Point _markerPressPosition;
    private bool _markerDragged;
    private const double MarkerDragThreshold = 4d;
    private static readonly TimeSpan s_defaultElementLength = TimeSpan.FromSeconds(5);
    private readonly ILogger _logger = Log.CreateLogger<TimelineTabView>();
    private readonly CompositeDisposable _disposables = [];
    private readonly Func<PointerWheelEventArgs, bool> _usesGestureAxes;
    private readonly Func<TopLevel, IDisposable?> _attachNativeInput;
    private TopLevel? _nativeInputRoot;
    private IDisposable? _nativeInputRegistration;
    private ElementView? _selectedElement;
    private CancellationTokenSource? _scrollCts;

    public TimelineTabView()
        : this(NativeScrollInput.UsesGestureAxes)
    {
    }

    internal TimelineTabView(Func<PointerWheelEventArgs, bool> usesGestureAxes, Func<TopLevel, IDisposable?>? attachNativeInput = null)
    {
        _usesGestureAxes = usesGestureAxes;
        _attachNativeInput = attachNativeInput ?? NativeScrollInput.Attach;
        InitializeComponent();

        gridSplitter.DragDelta += GridSplitter_DragDelta;

        RulerBar.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);
        ContentScroll.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);

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

        TimelinePanel.Children.RemoveRange(2, TimelinePanel.Children.Count - 2);
        _selectedElement = null;

        _disposables.Clear();
    }

    private void OnDataContextAttached(TimelineTabViewModel vm)
    {
        ViewModel = vm;

        TimelinePanel.Children.AddRange(vm.Elements.SelectMany(e =>
        {
            return new Control[]
            {
                new ElementView { DataContext = e }, new ElementScopeView { DataContext = e.Scope }
            };
        }));

        TimelinePanel.Children.AddRange(vm.Inlines.Select(e => new InlineAnimationLayer { DataContext = e }));

        vm.Elements.TrackCollectionChanged(
                AddElement,
                RemoveElement,
                () => { })
            .DisposeWith(_disposables);

        vm.Inlines.TrackCollectionChanged(
                OnAddedInline,
                OnRemovedInline,
                () => { })
            .DisposeWith(_disposables);

        ViewModel.ScrollTo.Subscribe(v => ScrollTimelinePosition(v.Range, v.ZIndex))
            .DisposeWith(_disposables);

        vm.CurrentTime
            .ObserveOnUIDispatcher()
            .Subscribe(OnCurrentTimeChangedForAutoScroll)
            .DisposeWith(_disposables);

        ViewModel.EditorContext.GetRequiredService<Beutl.Editor.Services.IEditorSelection>().SelectedObject.Subscribe(e =>
            {
                if (_selectedElement != null)
                {
                    ViewModel.ClearSelected();

                    _selectedElement = null;
                }

                if (e is Element element && FindElementView(element) is
                    { DataContext: ElementViewModel viewModel } newView)
                {
                    _selectedElement = newView;
                    ViewModel.SelectElement(viewModel);
                }
            })
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

    // 要素を追加
    private void AddElement(int index, ElementViewModel viewModel)
    {
        var view = new ElementView { DataContext = viewModel };
        var scopeView = new ElementScopeView { DataContext = viewModel.Scope };

        TimelinePanel.Children.Add(view);
        TimelinePanel.Children.Add(scopeView);
    }

    // 要素を削除
    private void RemoveElement(int index, ElementViewModel viewModel)
    {
        Element elm = viewModel.Model;

        for (int i = TimelinePanel.Children.Count - 1; i >= 0; i--)
        {
            Control item = TimelinePanel.Children[i];
            if ((item.DataContext is ElementViewModel vm1 && vm1.Model == elm)
                || (item.DataContext is ElementScopeViewModel vm2 && vm2.Model == elm))
            {
                TimelinePanel.Children.RemoveAt(i);
            }
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
        return TimelinePanel.Children.FirstOrDefault(ctr =>
            ctr.DataContext is ElementViewModel vm && vm.Model == element) as ElementView;
    }
}
