using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using FluentAvalonia.UI.Media;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public sealed class NodePortPoint : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<NodePortPoint, IBrush?>(nameof(Brush));

    public static readonly StyledProperty<bool> IsConnectedProperty =
        AvaloniaProperty.Register<NodePortPoint, bool>(nameof(IsConnected));

    private ConnectionLine? _line;
    private bool _captured;
    private bool _doubleClick;
    private Canvas? _canvas;
    private Point _dragStart;
    private Visual? _dragCoordinateSpace;

    static NodePortPoint()
    {
        AffectsRender<NodePortPoint>(BrushProperty, IsConnectedProperty);
    }

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public bool IsConnected
    {
        get => GetValue(IsConnectedProperty);
        set => SetValue(IsConnectedProperty, value);
    }

    public event EventHandler<NodePortConnectRequestedEventArgs>? ConnectRequested;

    public event EventHandler<NodePortConnectRequestedEventArgs>? DisconnectRequested;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _canvas = this.FindAncestorOfType<Canvas>();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _canvas = null;
    }

    protected override Size MeasureCore(Size availableSize)
    {
        return new Size(10, 10);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled) return;

        if (e.ClickCount == 2)
        {
            _doubleClick = true;
            e.Handled = true;
        }
        else if (_canvas != null && DataContext is NodePortViewModel viewModel)
        {
            PointerPoint point = e.GetCurrentPoint(_canvas);
            if (point.Properties.IsLeftButtonPressed
                && viewModel is not InputPortViewModel { CanConnect.Value: false })
            {
                _line = new ConnectionLine();
                Point? portPosition = this.TranslatePoint(new(5, 5), _canvas);
                if (viewModel is InputPortViewModel)
                {
                    _line.EndPoint = point.Position;
                    if (portPosition.HasValue)
                        _line.StartPoint = portPosition.Value;
                }
                else
                {
                    _line.StartPoint = point.Position;
                    if (portPosition.HasValue)
                        _line.EndPoint = portPosition.Value;
                }
                _line.SetNodePort(viewModel);
                _canvas.Children.Insert(0, _line);

                // Measure the gesture in window coordinates so zoom does not change its threshold.
                Visual coordinateSpace = (Visual?)TopLevel.GetTopLevel(this) ?? _canvas;
                _dragCoordinateSpace = coordinateSpace;
                _dragStart = e.GetPosition(coordinateSpace);
                e.Handled = true;
                _captured = true;
                e.Pointer.Capture(this);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_captured && !_doubleClick && _line != null)
        {
            if (DataContext is InputPortViewModel)
            {
                _line.EndPoint = e.GetPosition(_canvas);
            }
            else
            {
                _line.StartPoint = e.GetPosition(_canvas);
            }
            e.Handled = true;
        }
    }

    public bool TryConnect(PointerReleasedEventArgs e)
    {
        if (_canvas != null)
        {
            IInputElement? elm = _canvas!.InputHitTest(e.GetPosition(_canvas));
            if (elm != this && elm is NodePortPoint { DataContext: NodePortViewModel endViewModel })
            {
                var args = new NodePortConnectRequestedEventArgs(endViewModel, false);
                ConnectRequested?.Invoke(this, args);
                return args.IsConnected;
            }
        }

        return false;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_doubleClick)
        {
            DisconnectAll();
            _doubleClick = false;
            e.Handled = true;
        }
        else if (_captured)
        {
            Canvas? canvas = _canvas;
            Point releasePoint = canvas != null ? e.GetPosition(canvas) : default;
            Point releaseInWindow = _dragCoordinateSpace != null ? e.GetPosition(_dragCoordinateSpace) : default;
            bool connected = TryConnect(e);
            Disconnect();

            _line = null;
            e.Handled = true;
            _captured = false;
            e.Pointer.Capture(null);
            _dragCoordinateSpace = null;

            if (!connected && canvas != null && DataContext is NodePortViewModel { Model: not null } source
                && Math.Max(Math.Abs(releaseInWindow.X - _dragStart.X), Math.Abs(releaseInWindow.Y - _dragStart.Y)) >= 5
                && IsBlankDropTarget(canvas.InputHitTest(releasePoint)))
                this.FindAncestorOfType<NodeGraphView>()?.ShowCompatibleNodeMenu(source, releasePoint);
        }
    }

    private static bool IsBlankDropTarget(IInputElement? target)
    {
        if (target is not Visual visual) return true;
        return visual is not NodePortPoint and not GraphNodeView and not ConnectionLine
               && visual.FindAncestorOfType<GraphNodeView>() == null
               && visual.FindAncestorOfType<ConnectionLine>() == null;
    }

    private void Disconnect()
    {
        if (_line != null && _canvas != null)
        {
            _canvas.Children.Remove(_line);
            _line = null;
        }
    }

    private void DisconnectAll()
    {
        if (_canvas != null && DataContext is NodePortViewModel viewModel)
        {
            for (int i = _canvas.Children.Count - 1; i >= 0; i--)
            {
                Control item = _canvas.Children[i];
                if (item is ConnectionLine cline && cline.Match(viewModel))
                {
                    NodePortViewModel? target = cline.GetTarget(viewModel);
                    if (target != null)
                    {
                        var args = new NodePortConnectRequestedEventArgs(target, true)
                        {
                            Connection = Tag as ConnectionViewModel // IListPortの場合TagにConnectionViewModelを持っている
                        };
                        DisconnectRequested?.Invoke(this, args);
                        if (args.IsConnected)
                        {
                            continue;
                        }
                    }

                    _canvas.Children.Remove(cline);
                }
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        IBrush brush = Brush ?? Brushes.Teal;
        if (IsConnected)
        {
            context.FillRectangle(brush, new Rect(0, 0, 10, 10), 5);
        }
        else
        {
            if (brush is ISolidColorBrush solidColorBrush)
            {
                var color = (Color2)solidColorBrush.Color;
                color = color.WithSatf(color.Saturationf * 0.2f);
                context.FillRectangle(new ImmutableSolidColorBrush(color), new Rect(0, 0, 10, 10), 5);
            }
            else
            {
                context.FillRectangle(Brushes.Gray, new Rect(0, 0, 10, 10), 5);
            }
        }
    }
}
