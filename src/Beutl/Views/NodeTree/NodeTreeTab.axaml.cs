using Avalonia;
using Avalonia.Collections.Pooled;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
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

    public NodeTreeTab()
    {
        InitializeComponent();
        this.SubscribeDataContextChange<NodeTreeTabViewModel>(OnDataContextAttached, OnDataContextDetached);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(canvas);
        if (point.Properties.IsRightButtonPressed)
        {
            _rightClickedPosition = point.Position;
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            _leftClickedPosition = point.Position;
        }
    }

    private void AddRectClick(object? sender, RoutedEventArgs e)
    {
        AddNode(() => new RectNode());
    }

    private void AddOutputClick(object? sender, RoutedEventArgs e)
    {
        AddNode(() => new LayerOutputNode());
    }

    private void AddNode(Func<Node> factory)
    {
        if (DataContext is NodeTreeTabViewModel { Layer.Value: { } layer } viewModel)
        {
            Node node = factory();
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
