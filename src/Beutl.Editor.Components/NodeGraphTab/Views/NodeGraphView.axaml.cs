using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public partial class NodeGraphView : UserControl
{
    // GraphNodeView's Width in GraphNodeView.axaml.
    private const int GraphNodeWidth = 215;
    private readonly CompositeDisposable _disposables = [];
    private readonly Func<PointerWheelEventArgs, bool> _usesGestureAxes;
    private readonly Func<TopLevel, IDisposable?> _attachNativeInput;
    private TopLevel? _nativeInputRoot;
    private IDisposable? _nativeInputRegistration;
    private Point _rightClickedPosition;
    private bool _rangeSelectionPressed;
    private readonly List<(GraphNodeView GraphNode, bool IsSelectedOriginal)> _rangeSelection = [];
    private bool _matrixUpdating;
    private FAMenuFlyout? _portDropMenu;

    internal FAMenuFlyout? PortDropMenu => _portDropMenu;

    public NodeGraphView()
        : this(NativeScrollInput.UsesGestureAxes)
    {
    }

    internal NodeGraphView(Func<PointerWheelEventArgs, bool> usesGestureAxes, Func<TopLevel, IDisposable?>? attachNativeInput = null)
    {
        _usesGestureAxes = usesGestureAxes;
        _attachNativeInput = attachNativeInput ?? NativeScrollInput.Attach;
        InitializeComponent();
        InitializeMenuItems();
        this.SubscribeDataContextChange<NodeGraphViewModel>(OnDataContextAttached, OnDataContextDetached);

        AddHandler(PointerPressedEvent, OnNodeGraphPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnNodeGraphPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnNodeGraphPointerMoved, RoutingStrategies.Tunnel);

        zoomBorder.AddHandler(KeyDownEvent, OnGraphKeyDown, RoutingStrategies.Tunnel);
        zoomBorder.AddHandler(PointerWheelChangedEvent, OnGraphPointerWheelChanged, RoutingStrategies.Tunnel);
        // Register before ZoomBorder attaches its built-in magnify handler.
        zoomBorder.AddHandler(PointerTouchPadGestureMagnifyEvent, OnGraphMagnify);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DropEvent, OnDrop);
        zoomBorder.ZoomChanged += OnZoomChanged;
    }

    private void OnGraphKeyDown(object? sender, KeyEventArgs e)
    {
        // Leave the event unhandled so the editor still receives key and text input.
        zoomBorder.EnableKeyboardNavigation = !ContextCommandInput.IsFromTextInput(e);
    }

    private void OnGraphPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers == KeyGestureHelper.GetCommandModifier())
        {
            Point point = e.GetPosition(canvas);
            zoomBorder.ZoomDeltaTo(e.Delta.Y, point.X, point.Y, true);
            e.Handled = true;
        }
        else if (_usesGestureAxes(e))
        {
            // Match the timeline's scroll distance and preserve the OS gesture axes/direction.
            zoomBorder.PanDelta(e.Delta.X * 50, e.Delta.Y * 50, true);
            e.Handled = true;
        }
    }

    private void OnGraphMagnify(object? sender, PointerDeltaEventArgs e)
    {
        // Native magnification is a relative scale change, not a mouse-wheel delta.
        double factor = 1 + e.Delta.X;
        if (double.IsFinite(factor) && factor > 0)
        {
            Point point = e.GetPosition(canvas);
            zoomBorder.ZoomTo(factor, point.X, point.Y, true);
        }
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is NodeGraphViewModel viewModel
            && e.DataTransfer.TryGetValue(BeutlDataFormats.GraphNode) is { } typeName
            && TypeFormat.ToType(typeName) is { } item)
        {
            Point point = e.GetPosition(canvas) - new Point(GraphNodeWidth / 2, 0);
            viewModel.AddNodePort(item, point);
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(BeutlDataFormats.GraphNode))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnZoomChanged(object sender, ZoomChangedEventArgs e)
    {
        if (DataContext is NodeGraphViewModel viewModel)
        {
            _matrixUpdating = true;
            viewModel.Matrix.Value = zoomBorder.Matrix;
            _matrixUpdating = false;
        }

        if (_rangeSelectionPressed)
        {
            UpdateRangeSelection();
        }
    }

    // VisualTreeからデタッチされて、再度アタッチされた後のレイアウトでZoomBorderのMatrixがリセットされてしまうため、レイアウト更新後にMatrixを再設定する
    private Matrix? _initialMatrix;

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
        _initialMatrix = (DataContext as NodeGraphViewModel)?.Matrix.Value;
        LayoutUpdated += OnLayoutUpdated;

        void OnLayoutUpdated(object? sender, EventArgs eventArgs)
        {
            if (_initialMatrix.HasValue)
            {
                zoomBorder.SetMatrix(_initialMatrix.Value, true);
            }
            LayoutUpdated -= OnLayoutUpdated;
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

    private void UpdateRangeSelection()
    {
        foreach ((GraphNodeView? node, bool isSelectedOriginal) in _rangeSelection)
        {
            if (node.DataContext is GraphNodeViewModel nodeViewModel)
            {
                nodeViewModel.IsSelected.Value = isSelectedOriginal;
            }
        }

        _rangeSelection.Clear();
        Rect rect = overlay.SelectionRange.Normalize();

        foreach (Control item in canvas.Children)
        {
            if (item is GraphNodeView { DataContext: GraphNodeViewModel nodeViewModel } nodeView)
            {
                var bounds = new Rect(nodeView.GetPoint(), nodeView.Bounds.Size);
                if (rect.Intersects(bounds))
                {
                    _rangeSelection.Add((nodeView, nodeViewModel.IsSelected.Value));
                    nodeViewModel.IsSelected.Value = true;
                }
            }
        }
    }

    private void OnNodeGraphPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_rangeSelectionPressed)
        {
            PointerPoint point = e.GetCurrentPoint(canvas);
            Rect rect = overlay.SelectionRange;
            overlay.SelectionRange = new(rect.Position, point.Position);
            UpdateRangeSelection();
        }
    }

    private void OnNodeGraphPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_rangeSelectionPressed)
        {
            overlay.SelectionRange = default;
            _rangeSelection.Clear();
            _rangeSelectionPressed = false;
        }
    }

    private void OnNodeGraphPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(canvas);
        if (point.Properties.IsRightButtonPressed)
        {
            _rightClickedPosition = point.Position;
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            if (e.KeyModifiers == KeyModifiers.Control
                && e.Source is ZoomBorder)
            {
                _rangeSelectionPressed = true;
                overlay.SelectionRange = new(point.Position, default(Size));
                e.Handled = true;
            }
        }
    }

    private void InitializeMenuItems()
    {
        foreach (GraphNodeRegistry.BaseRegistryItem item in GraphNodeRegistry.GetRegistered())
        {
            addNode.Items.Add(CreateNodeMenuItem(item));
        }
    }

    private Control CreateNodeMenuItem(GraphNodeRegistry.BaseRegistryItem item)
    {
        if (item is GraphNodeRegistry.GroupableRegistryItem group)
        {
            var subMenu = new FAMenuFlyoutSubItem { Text = item.DisplayName };
            foreach (GraphNodeRegistry.BaseRegistryItem child in group.Items)
                subMenu.Items.Add(CreateNodeMenuItem(child));
            return subMenu;
        }

        var menuItem = new FAMenuFlyoutItem { Text = item.DisplayName, DataContext = item };
        menuItem.Click += AddNodeClick;
        return menuItem;
    }

    internal void ShowCompatibleNodeMenu(NodePortViewModel source, Point canvasPoint)
    {
        if (DataContext is not NodeGraphViewModel viewModel
            || source.GraphNodeViewModel.NodeGraphViewModel != viewModel
            || !viewModel.SupportsConnectedNodeCreation
            || source.Model is not { } sourcePort
            || canvas.TranslatePoint(canvasPoint, zoomBorder) is not { } viewportPoint
            || !new Rect(zoomBorder.Bounds.Size).Contains(viewportPoint))
            return;

        _portDropMenu?.Hide();
        IList<GraphNodeRegistry.BaseRegistryItem> registered = GraphNodeRegistry.GetRegistered();
        var candidates = CompatibleNodeFinder.Find(viewModel.NodeGraph, sourcePort, registered);
        FAMenuFlyout? menu = null;
        var items = new List<Control>();
        foreach (GraphNodeRegistry.BaseRegistryItem registry in registered)
        {
            if (CreateMenuItem(registry) is { } item) items.Add(item);
        }
        if (items.Count == 0) return;

        menu = new FAMenuFlyout { Placement = PlacementMode.Pointer };
        foreach (var item in items) menu.Items.Add(item);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_portDropMenu, menu)) _portDropMenu = null;
        };
        _portDropMenu = menu;
        menu.ShowAt(canvas, true);

        Control? CreateMenuItem(GraphNodeRegistry.BaseRegistryItem registry)
        {
            if (registry is GraphNodeRegistry.GroupableRegistryItem group)
            {
                var children = new List<Control>();
                foreach (GraphNodeRegistry.BaseRegistryItem child in group.Items)
                {
                    if (CreateMenuItem(child) is { } item) children.Add(item);
                }
                if (children.Count == 0) return null;
                var subMenu = new FAMenuFlyoutSubItem { Text = group.DisplayName };
                foreach (var item in children) subMenu.Items.Add(item);
                return subMenu;
            }

            if (registry is not GraphNodeRegistry.RegistryItem nodeRegistry
                || !candidates.TryGetValue(nodeRegistry, out CompatibleNodeFinder.Candidate? candidate))
                return null;

            if (candidate.Ports.Count == 1)
            {
                var nodeItem = new FAMenuFlyoutItem { Text = nodeRegistry.DisplayName };
                CompatibleNodeFinder.PortChoice? port = candidate.Ports[0];
                nodeItem.Click += (_, _) => AddConnectedNode(candidate, port);
                return nodeItem;
            }
            else
            {
                var nodeItem = new FAMenuFlyoutSubItem { Text = nodeRegistry.DisplayName };
                foreach (CompatibleNodeFinder.PortChoice? port in candidate.Ports)
                {
                    var portItem = new FAMenuFlyoutItem { Text = port!.DisplayName };
                    portItem.Click += (_, _) => AddConnectedNode(candidate, port);
                    nodeItem.Items.Add(portItem);
                }
                return nodeItem;
            }
        }

        void AddConnectedNode(CompatibleNodeFinder.Candidate candidate, CompatibleNodeFinder.PortChoice? choice)
        {
            try
            {
                if (DataContext == viewModel && source.Model == sourcePort)
                    viewModel.TryAddSuggestedNode(candidate, choice, sourcePort,
                        new Point(canvasPoint.X - GraphNodeWidth / 2d, canvasPoint.Y));
            }
            finally
            {
                menu?.Hide();
            }
        }
    }

    private void AddNodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeGraphViewModel viewModel
            && sender is FAMenuFlyoutItem { DataContext: GraphNodeRegistry.RegistryItem item })
        {
            viewModel.AddNodePort(item.Type, _rightClickedPosition);
        }
    }

    // Listed each time the menu opens, however it is opened: templates are files, saved from any graph.
    private void CanvasMenuOpened(object? sender, EventArgs e)
    {
        if (DataContext is not NodeGraphViewModel viewModel)
            return;

        templatesMenu.Items.Clear();
        foreach (Beutl.NodeGraph.Nodes.Group.GroupNodeTemplate template in viewModel.Templates.List())
        {
            var item = new FAMenuFlyoutItem { Text = template.Name };
            item.Click += (_, _) => viewModel.AddTemplate(template, _rightClickedPosition);
            templatesMenu.Items.Add(item);
        }

        if (templatesMenu.Items.Count == 0)
            templatesMenu.Items.Add(new FAMenuFlyoutItem { Text = NodeGraphStrings.Template_None, IsEnabled = false });
    }

    private void RunGenerativeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeGraphViewModel viewModel)
            _ = viewModel.RunGenerativeAsync(null, force: false);
    }

    private void StopGenerativeClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as NodeGraphViewModel)?.CancelGenerative();
    }

    private void ResetZoomClick(object? sender, RoutedEventArgs e)
    {
        zoomBorder.Zoom(1, zoomBorder.OffsetX, zoomBorder.OffsetY);
    }

    internal static ConnectionLine CreateLine(ConnectionViewModel connVM)
    {
        return new ConnectionLine()
        {
            [!Line.StartPointProperty] = connVM.InputPortPosition.ToBinding(),
            [!Line.EndPointProperty] = connVM.OutputPortPosition.ToBinding(),
            [!ConnectionLine.InputPortProperty] = connVM.InputPortVM.ToBinding(),
            [!ConnectionLine.OutputPortProperty] = connVM.OutputPortVM.ToBinding(),
            [!IsVisibleProperty] = connVM.InputPortVM.CombineLatest(connVM.OutputPortVM,
                (input, output) => input != null && output != null).ToBinding(),
            ConnectionViewModel = connVM
        };
    }

    private void InitializeConnectionPositions(ConnectionViewModel connVM)
    {
        foreach (Control child in canvas.Children)
        {
            if (child is GraphNodeView { DataContext: GraphNodeViewModel nodeVM } nodeView)
            {
                bool hasInput = nodeVM.GraphNode.EnumerateMembers().Any(i => i.Id == connVM.Connection.Input.Id);
                bool hasOutput = nodeVM.GraphNode.EnumerateMembers().Any(i => i.Id == connVM.Connection.Output.Id);

                if (hasInput || hasOutput)
                {
                    nodeView.UpdateNodePortPosition();
                    if (hasInput && hasOutput) break;
                }
            }
        }
    }

    private void OnDataContextAttached(NodeGraphViewModel obj)
    {
        obj.Nodes.ForEachItem(
                node =>
                {
                    var control = new GraphNodeView() { DataContext = node };
                    canvas.Children.Add(control);
                },
                node =>
                {
                    Control? control = canvas.Children.FirstOrDefault(x => x.DataContext == node);
                    if (control != null)
                    {
                        canvas.Children.Remove(control);
                    }
                },
                () => RemoveCanvasChildren<GraphNodeView>())
            .DisposeWith(_disposables);

        obj.AllConnections.ForEachItem(
                connVM =>
                {
                    ConnectionLine line = CreateLine(connVM);
                    canvas.Children.Insert(0, line);
                    InitializeConnectionPositions(connVM);
                },
                connVM =>
                {
                    for (int i = canvas.Children.Count - 1; i >= 0; i--)
                    {
                        if (canvas.Children[i] is ConnectionLine line && line.ConnectionViewModel == connVM)
                        {
                            canvas.Children.RemoveAt(i);
                            break;
                        }
                    }
                },
                () => RemoveCanvasChildren<ConnectionLine>())
            .DisposeWith(_disposables);

        obj.Matrix.Where(_ => !_matrixUpdating)
            .Subscribe(m => zoomBorder.SetMatrix(m, true))
            .DisposeWith(_disposables);
    }

    private void RemoveCanvasChildren<T>()
        where T : Control
    {
        for (int i = canvas.Children.Count - 1; i >= 0; i--)
        {
            if (canvas.Children[i] is T)
            {
                canvas.Children.RemoveAt(i);
            }
        }
    }

    private void OnDataContextDetached(NodeGraphViewModel obj)
    {
        _portDropMenu?.Hide();
        _portDropMenu = null;
        _disposables.Clear();
        canvas.Children.Clear();
    }
}
