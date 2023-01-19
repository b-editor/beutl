using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

using Beutl.NodeTree.Nodes;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

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
        if (DataContext is NodeTreeTabViewModel { Layer.Value: { } layer } viewModel)
        {
            var node = new RectNode();
            layer.Space.Nodes.Add(node);
            NodeViewModel? nodeViewModel = viewModel.Nodes.FirstOrDefault(x => x.Node == node);
            if (nodeViewModel != null)
            {
                nodeViewModel.Position.Value = _rightClickedPosition;
            }
        }
    }

    private void ResetZoomClick(object? sender, RoutedEventArgs e)
    {
        zoomBorder.Zoom(1, zoomBorder.OffsetX, zoomBorder.OffsetY);
    }

    private void OnDataContextAttached(NodeTreeTabViewModel obj)
    {
        _disposable = obj.Nodes.ForEachItem(
            item =>
            {
                var control = new NodeView()
                {
                    DataContext = item
                };
                canvas.Children.Add(control);
            },
            item =>
            {
                IControl? control = canvas.Children.FirstOrDefault(x => x.DataContext == item);
                if (control != null)
                {
                    canvas.Children.Remove(control);
                }
            },
            canvas.Children.Clear);
    }

    private void OnDataContextDetached(NodeTreeTabViewModel obj)
    {
        _disposable?.Dispose();
    }
}
