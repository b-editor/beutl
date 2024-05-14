using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Collections.Pooled;
using Beutl.NodeTree;
using Beutl.Services;
using Beutl.ViewModels.NodeTree;

namespace Beutl.Views.NodeTree;

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
            && e.Data.Contains(KnownLibraryItemFormats.Node)
            && e.Data.Get(KnownLibraryItemFormats.Node) is Type item)
        {
            Point point = e.GetPosition(canvas) - new Point(215 / 2, 0);
            viewModel.AddSocket(item, point);
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(KnownLibraryItemFormats.Node))
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

    internal static ConnectionLine CreateLine(InputSocketViewModel input, OutputSocketViewModel output)
    {
        return new ConnectionLine() { [!Line.StartPointProperty] = input.SocketPosition.ToBinding(), [!Line.EndPointProperty] = output.SocketPosition.ToBinding(), InputSocket = input, OutputSocket = output };
    }

    private void OnDataContextAttached(NodeTreeViewModel obj)
    {
        obj.Nodes.ForEachItem(
                node =>
                {
                    var control = new NodeView() { DataContext = node };
                    canvas.Children.Add(control);

                    using var list = new PooledList<Connection>();
                    foreach (NodeItemViewModel item in node.Items)
                    {
                        if (item is InputSocketViewModel { Model.Connection: { } connection })
                        {
                            list.Add(connection);
                        }
                        else if (item is OutputSocketViewModel { Model: { } outputSocket })
                        {
                            list.AddRange(outputSocket.Connections);
                        }
                    }

                    foreach (Connection connection in list.Span)
                    {
                        if (!canvas.Children.OfType<ConnectionLine>().Any(x => x.Match(connection.Input, connection.Output)) &&
                            obj.FindSocketViewModel(connection.Input) is InputSocketViewModel inputViewModel &&
                            obj.FindSocketViewModel(connection.Output) is OutputSocketViewModel outputViewModel)
                        {
                            ConnectionLine line = CreateLine(inputViewModel, outputViewModel);
                            canvas.Children.Insert(0, line);
                        }
                    }
                },
                node =>
                {
                    Control? control = canvas.Children.FirstOrDefault(x => x.DataContext == node);
                    if (control != null)
                    {
                        canvas.Children.Remove(control);
                    }

                    foreach (NodeItemViewModel item in node.Items)
                    {
                        if (item is InputSocketViewModel { Model: { } input })
                        {
                            ConnectionLine? line = canvas.Children.OfType<ConnectionLine>()
                                .FirstOrDefault(x => x.Match(input));
                            if (line != null)
                            {
                                canvas.Children.Remove(line);
                            }
                        }
                        else if (item is OutputSocketViewModel { Model: { } output })
                        {
                            canvas.Children.RemoveAll(canvas.Children.OfType<ConnectionLine>().Where(x => x.Match(output)));
                        }
                    }
                },
                canvas.Children.Clear)
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
