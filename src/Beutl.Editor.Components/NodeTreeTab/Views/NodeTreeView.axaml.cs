using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Helpers;
using Beutl.NodeTree;
using Beutl.Editor.Components.NodeTreeTab.ViewModels;

using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.NodeTreeTab.Views;

public partial class NodeTreeView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private Point _rightClickedPosition;
    internal Point _leftClickedPosition;
    private bool _rangeSelectionPressed;
    private readonly List<(NodeView Node, bool IsSelectedOriginal)> _rangeSelection = [];
    private bool _matrixUpdating;

    public NodeTreeView()
    {
        InitializeComponent();
        InitializeMenuItems();
        this.SubscribeDataContextChange<NodeTreeViewModel>(OnDataContextAttached, OnDataContextDetached);

        AddHandler(PointerPressedEvent, OnNodeTreePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnNodeTreePointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnNodeTreePointerMoved, RoutingStrategies.Tunnel);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DropEvent, OnDrop);
        zoomBorder.ZoomChanged += OnZoomChanged;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is NodeTreeViewModel viewModel
            && e.DataTransfer.TryGetValue(BeutlDataFormats.Node) is { } typeName
            && TypeFormat.ToType(typeName) is { } item)
        {
            Point point = e.GetPosition(canvas) - new Point(215 / 2, 0);
            viewModel.AddSocket(item, point);
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(BeutlDataFormats.Node))
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
        if (DataContext is NodeTreeViewModel viewModel)
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

    private void UpdateRangeSelection()
    {
        foreach ((NodeView? node, bool isSelectedOriginal) in _rangeSelection)
        {
            if (node.DataContext is NodeViewModel nodeViewModel)
            {
                nodeViewModel.IsSelected.Value = isSelectedOriginal;
            }
        }

        _rangeSelection.Clear();
        Rect rect = overlay.SelectionRange.Normalize();

        foreach (Control item in canvas.Children)
        {
            if (item is NodeView { DataContext: NodeViewModel nodeViewModel } nodeView)
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

    private void OnNodeTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_rangeSelectionPressed)
        {
            PointerPoint point = e.GetCurrentPoint(canvas);
            Rect rect = overlay.SelectionRange;
            overlay.SelectionRange = new(rect.Position, point.Position);
            UpdateRangeSelection();
        }
    }

    private void OnNodeTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_rangeSelectionPressed)
        {
            overlay.SelectionRange = default;
            _rangeSelection.Clear();
            _rangeSelectionPressed = false;
        }
    }

    private void OnNodeTreePointerPressed(object? sender, PointerPressedEventArgs e)
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

        foreach (NodeRegistry.BaseRegistryItem item in NodeRegistry.GetRegistered())
        {
            var menuItem = new MenuItem { Header = item.DisplayName, DataContext = item, };
            menuItem.Click += AddNodeClick;
            menulist.Add(menuItem);

            if (item is NodeRegistry.GroupableRegistryItem groupable)
            {
                Add(menuItem, groupable);
            }
        }
    }

    private void Add(MenuItem menuItem, NodeRegistry.GroupableRegistryItem list)
    {
        var alist = new AvaloniaList<MenuItem>();
        menuItem.ItemsSource = alist;
        foreach (NodeRegistry.BaseRegistryItem item in list.Items)
        {
            var menuItem2 = new MenuItem { Header = item.DisplayName, DataContext = item, };

            if (item is NodeRegistry.GroupableRegistryItem inner)
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
        if (DataContext is NodeTreeViewModel viewModel
            && sender is MenuItem { DataContext: NodeRegistry.RegistryItem item })
        {
            viewModel.AddSocket(item.Type, _rightClickedPosition);
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
            [!Line.StartPointProperty] = connVM.InputSocketPosition.ToBinding(),
            [!Line.EndPointProperty] = connVM.OutputSocketPosition.ToBinding(),
            ConnectionViewModel = connVM,
            InputSocket = connVM.InputSocketVM,
            OutputSocket = connVM.OutputSocketVM
        };
    }

    private void InitializeConnectionPositions(ConnectionViewModel connVM)
    {
        foreach (Control child in canvas.Children)
        {
            if (child is NodeView { DataContext: NodeViewModel nodeVM } nodeView)
            {
                bool hasInput = nodeVM.Items.Contains(connVM.InputSocketVM);
                bool hasOutput = nodeVM.Items.Contains(connVM.OutputSocketVM);

                if (hasInput || hasOutput)
                {
                    nodeView.UpdateSocketPosition();
                    if (hasInput && hasOutput) break;
                }
            }
        }
    }

    private void OnDataContextAttached(NodeTreeViewModel obj)
    {
        obj.Nodes.ForEachItem(
                node =>
                {
                    var control = new NodeView() { DataContext = node };
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
                        if (canvas.Children[i] is NodeView)
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

    private void OnDataContextDetached(NodeTreeViewModel obj)
    {
        _disposables.Clear();
        canvas.Children.Clear();
    }
}
