using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.NodeGraph;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public partial class NodeGraphView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private Point _rightClickedPosition;
    internal Point _leftClickedPosition;
    private bool _rangeSelectionPressed;
    private readonly List<(GraphNodeView GraphNode, bool IsSelectedOriginal)> _rangeSelection = [];
    private bool _matrixUpdating;
    private ContextMenu? _portDropMenu;

    internal ContextMenu? PortDropMenu => _portDropMenu;

    public NodeGraphView()
    {
        InitializeComponent();
        InitializeMenuItems();
        this.SubscribeDataContextChange<NodeGraphViewModel>(OnDataContextAttached, OnDataContextDetached);

        AddHandler(PointerPressedEvent, OnNodeGraphPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnNodeGraphPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnNodeGraphPointerMoved, RoutingStrategies.Tunnel);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DropEvent, OnDrop);
        zoomBorder.ZoomChanged += OnZoomChanged;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is NodeGraphViewModel viewModel
            && e.DataTransfer.TryGetValue(BeutlDataFormats.GraphNode) is { } typeName
            && TypeFormat.ToType(typeName) is { } item)
        {
            Point point = e.GetPosition(canvas) - new Point(215 / 2, 0);
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
            _leftClickedPosition = point.Position;

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
        var menulist = new AvaloniaList<MenuItem>();
        addNode.ItemsSource = menulist;

        foreach (GraphNodeRegistry.BaseRegistryItem item in GraphNodeRegistry.GetRegistered())
        {
            var menuItem = new MenuItem { Header = item.DisplayName, DataContext = item, };
            menuItem.Click += AddNodeClick;
            menulist.Add(menuItem);

            if (item is GraphNodeRegistry.GroupableRegistryItem groupable)
            {
                Add(menuItem, groupable);
            }
        }
    }

    internal void ShowCompatibleNodeMenu(NodePortViewModel source, Point canvasPoint)
    {
        if (DataContext is not NodeGraphViewModel viewModel
            || source.GraphNodeViewModel.NodeGraphViewModel != viewModel
            || source.Model is not { } sourcePort
            || canvas.TranslatePoint(canvasPoint, zoomBorder) is not { } viewportPoint
            || !new Rect(zoomBorder.Bounds.Size).Contains(viewportPoint))
            return;

        _portDropMenu?.Close();
        IList<GraphNodeRegistry.BaseRegistryItem> registered = GraphNodeRegistry.GetRegistered();
        var candidates = CompatibleNodeFinder.Find(viewModel.NodeGraph, sourcePort, registered);
        ContextMenu? menu = null;
        var items = new List<MenuItem>();
        foreach (GraphNodeRegistry.BaseRegistryItem registry in registered)
        {
            if (CreateMenuItem(registry) is { } item) items.Add(item);
        }
        if (items.Count == 0) return;

        menu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer };
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_portDropMenu, menu)) _portDropMenu = null;
        };
        _portDropMenu = menu;
        menu.Open(canvas);

        MenuItem? CreateMenuItem(GraphNodeRegistry.BaseRegistryItem registry)
        {
            if (registry is GraphNodeRegistry.GroupableRegistryItem group)
            {
                var children = new List<MenuItem>();
                foreach (GraphNodeRegistry.BaseRegistryItem child in group.Items)
                {
                    if (CreateMenuItem(child) is { } item) children.Add(item);
                }
                return children.Count == 0 ? null : new MenuItem
                {
                    Header = group.DisplayName,
                    ItemsSource = children
                };
            }

            if (registry is not GraphNodeRegistry.RegistryItem nodeRegistry
                || !candidates.TryGetValue(nodeRegistry, out CompatibleNodeFinder.Candidate? candidate))
                return null;

            var nodeItem = new MenuItem { Header = nodeRegistry.DisplayName };
            if (candidate.Ports.Count == 1)
            {
                INodePort? port = candidate.Ports[0];
                nodeItem.Click += (_, _) => AddConnectedNode(candidate.Node, port);
            }
            else
            {
                var ports = new List<MenuItem>(candidate.Ports.Count);
                foreach (INodePort? port in candidate.Ports)
                {
                    var portItem = new MenuItem { Header = PortDisplayName(port!) };
                    portItem.Click += (_, _) => AddConnectedNode(candidate.Node, port);
                    ports.Add(portItem);
                }
                nodeItem.ItemsSource = ports;
            }
            return nodeItem;
        }

        void AddConnectedNode(GraphNode node, INodePort? port)
        {
            if (DataContext == viewModel && source.Model == sourcePort)
                viewModel.AddNodeAndConnect(node, new Point(canvasPoint.X - 215 / 2d, canvasPoint.Y), sourcePort, port);
            menu?.Close();
        }
    }

    private static string PortDisplayName(INodePort port)
    {
        string name = port.Display?.GetName() ?? port.Name;
        if (port is INestedInputPort { RootMember.Value: { } root })
            return $"{root.Display?.GetName() ?? root.Name} / {name}";
        return name;
    }

    private void Add(MenuItem menuItem, GraphNodeRegistry.GroupableRegistryItem list)
    {
        var alist = new AvaloniaList<MenuItem>();
        menuItem.ItemsSource = alist;
        foreach (GraphNodeRegistry.BaseRegistryItem item in list.Items)
        {
            var menuItem2 = new MenuItem { Header = item.DisplayName, DataContext = item, };

            if (item is GraphNodeRegistry.GroupableRegistryItem inner)
            {
                Add(menuItem2, inner);
            }
            else
            {
                menuItem2.Click += AddNodeClick;
            }

            alist.Add(menuItem2);
        }
    }

    private void AddNodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeGraphViewModel viewModel
            && sender is MenuItem { DataContext: GraphNodeRegistry.RegistryItem item })
        {
            viewModel.AddNodePort(item.Type, _rightClickedPosition);
        }
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
                () =>
                {
                    for (int i = canvas.Children.Count - 1; i >= 0; i--)
                    {
                        if (canvas.Children[i] is GraphNodeView)
                        {
                            canvas.Children.RemoveAt(i);
                        }
                    }
                })
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
                () =>
                {
                    for (int i = canvas.Children.Count - 1; i >= 0; i--)
                    {
                        if (canvas.Children[i] is ConnectionLine)
                        {
                            canvas.Children.RemoveAt(i);
                        }
                    }
                })
            .DisposeWith(_disposables);

        obj.Matrix.Where(_ => !_matrixUpdating)
            .Subscribe(m => zoomBorder.SetMatrix(m, true))
            .DisposeWith(_disposables);
    }

    private void OnDataContextDetached(NodeGraphViewModel obj)
    {
        _portDropMenu?.Close();
        _portDropMenu = null;
        _disposables.Clear();
        canvas.Children.Clear();
    }
}
