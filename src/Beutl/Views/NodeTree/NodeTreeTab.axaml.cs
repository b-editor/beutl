using Avalonia;
using Avalonia.Collections;
using Avalonia.Collections.Pooled;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;

using Beutl.NodeTree;
using Beutl.NodeTree.Nodes;
using Beutl.ViewModels.NodeTree;

namespace Beutl.Views.NodeTree;

public partial class NodeTreeTab : UserControl
{
    private IDisposable? _disposable;
    private Point _rightClickedPosition;
    internal Point _leftClickedPosition;
    private bool _rangeSelectionPressed;
    private List<(NodeView Node, bool IsSelectedOriginal)> _rangeSelection = new();

    public NodeTreeTab()
    {
        InitializeComponent();
        InitializeMenuItems();
        this.SubscribeDataContextChange<NodeTreeTabViewModel>(OnDataContextAttached, OnDataContextDetached);

        AddHandler(PointerPressedEvent, OnNodeTreePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnNodeTreePointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnNodeTreePointerMoved, RoutingStrategies.Tunnel);
        zoomBorder.ZoomChanged += OnZoomChanged;
    }

    private void OnZoomChanged(object sender, ZoomChangedEventArgs e)
    {
        if (_rangeSelectionPressed)
        {
            UpdateRangeSelection();
        }
    }

    private void UpdateRangeSelection()
    {
        foreach ((var node, bool isSelectedOriginal) in _rangeSelection)
        {
            if (node.DataContext is NodeViewModel nodeViewModel)
            {
                nodeViewModel.IsSelected.Value = isSelectedOriginal;
            }
        }

        _rangeSelection.Clear();
        Rect rect = overlay.SelectionRange.Normalize();

        foreach (IControl item in canvas.Children)
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
                overlay.SelectionRange = new(point.Position, Size.Empty);
                e.Handled = true;
            }
        }
    }

    private void InitializeMenuItems()
    {
        var menulist = new AvaloniaList<MenuItem>();
        addNode.Items = menulist;

        foreach (NodeRegistry.BaseRegistryItem item in NodeRegistry.GetRegistered())
        {
            var menuItem = new MenuItem
            {
                Header = item.DisplayName,
                DataContext = item,
            };
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
        menuItem.Items = alist;
        foreach (NodeRegistry.BaseRegistryItem item in list.Items)
        {
            var menuItem2 = new MenuItem
            {
                Header = item.DisplayName,
                DataContext = item,
            };

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
        if (sender is MenuItem { DataContext: NodeRegistry.RegistryItem item })
        {
            AddNode((Node)Activator.CreateInstance(item.Type)!);
        }
    }

    private void AddNode(Node node)
    {
        if (DataContext is NodeTreeTabViewModel { Layer.Value: { } layer } viewModel)
        {
            node.Position = (_rightClickedPosition.X, _rightClickedPosition.Y);
            layer.Space.Nodes.Add(node);
        }
    }

    private void ResetZoomClick(object? sender, RoutedEventArgs e)
    {
        zoomBorder.Zoom(1, zoomBorder.OffsetX, zoomBorder.OffsetY);
    }

    internal static ConnectionLine CreateLine(SocketViewModel first, SocketViewModel second)
    {
        return new ConnectionLine()
        {
            [!Line.StartPointProperty] = first.SocketPosition.ToBinding(),
            [!Line.EndPointProperty] = second.SocketPosition.ToBinding(),
            Stroke = Brushes.White,
            StrokeThickness = 3,
            First = first.Model,
            Second = second.Model
        };
    }

    private void OnDataContextAttached(NodeTreeTabViewModel obj)
    {
        _disposable = obj.Nodes.ForEachItem(
            node =>
            {
                var control = new NodeView()
                {
                    DataContext = node
                };
                canvas.Children.Add(control);

                using var list = new PooledList<IConnection>();
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

                foreach (IConnection connection in list.Span)
                {
                    if (!canvas.Children.OfType<ConnectionLine>().Any(x => x.Match(connection.Input, connection.Output)))
                    {
                        SocketViewModel? first = obj.FindSocketViewModel(connection.Input);
                        SocketViewModel? second = obj.FindSocketViewModel(connection.Output);
                        if (first != null && second != null)
                        {
                            ConnectionLine line = CreateLine(first, second);
                            canvas.Children.Insert(0, line);
                        }
                    }
                }
            },
            node =>
            {
                IControl? control = canvas.Children.FirstOrDefault(x => x.DataContext == node);
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
            canvas.Children.Clear);
    }

    private void OnDataContextDetached(NodeTreeTabViewModel obj)
    {
        _disposable?.Dispose();
    }
}
