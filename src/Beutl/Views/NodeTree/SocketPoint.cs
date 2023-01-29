using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using Beutl.NodeTree;
using Beutl.ViewModels.NodeTree;

namespace Beutl.Views.NodeTree;

public enum SocketState
{
    Disconnected,
    Connected,
    Invalid,
}

public class SocketConnectRequestedEventArgs : EventArgs
{
    public SocketConnectRequestedEventArgs(ISocket target, SocketState state)
    {
        Target = target;
        State = state;
    }

    public ISocket Target { get; }

    public SocketState State { get; set; }
}

public sealed class ConnectionLine : Line
{
    public ISocket? First { get; set; }

    public ISocket? Second { get; set; }
}

public sealed class SocketPoint : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<SocketPoint, IBrush?>(nameof(Brush));

    public static readonly StyledProperty<SocketState> StateProperty =
        AvaloniaProperty.Register<SocketPoint, SocketState>(nameof(State));

    private ConnectionLine? _line;
    private IBrush? _criticalBrush;
    private IDisposable? _disposable;
    private bool _captured;
    private bool _doubleClick;
    private Canvas? _canvas;

    static SocketPoint()
    {
        AffectsRender<SocketPoint>(BrushProperty, StateProperty);
    }

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public SocketState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public event EventHandler<SocketConnectRequestedEventArgs>? ConnectRequested;

    public event EventHandler<SocketConnectRequestedEventArgs>? DisconnectRequested;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _disposable = this.GetResourceObservable("SystemFillColorCriticalBrush").Subscribe(x =>
        {
            _criticalBrush = x as IBrush;
            InvalidateVisual();
        });
        _canvas = this.FindAncestorOfType<Canvas>();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _disposable?.Dispose();
        _canvas = null;
    }

    protected override Size MeasureCore(Size availableSize)
    {
        return new Size(10, 10);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.ClickCount == 2)
        {
            _doubleClick = true;
            e.Handled = true;
        }
        else if (_canvas != null && DataContext is SocketViewModel viewModel)
        {
            _line = new ConnectionLine()
            {
                [!Line.StartPointProperty] = viewModel.SocketPosition.ToBinding(),
                EndPoint = e.GetPosition(_canvas),
                Stroke = Brushes.White,
                StrokeThickness = 3,
                First = viewModel.Model
            };
            _canvas.Children.Insert(0, _line);

            e.Handled = true;
            _captured = true;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_captured && !_doubleClick && _line != null)
        {
            _line.EndPoint = e.GetPosition(_canvas);
            e.Handled = true;
        }
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
            if (_canvas != null)
            {
                IInputElement? elm = _canvas!.InputHitTest(e.GetPosition(_canvas));
                if (elm != this && elm is SocketPoint { DataContext: SocketViewModel endViewModel })
                {
                    if (_line != null)
                    {
                        _line.Bind(Line.EndPointProperty, endViewModel.SocketPosition.ToBinding());
                        _line.Second = endViewModel.Model;
                    }

                    var args = new SocketConnectRequestedEventArgs(endViewModel.Model, State);
                    ConnectRequested?.Invoke(this, args);
                    if (args.State == SocketState.Disconnected)
                    {
                        Disconnect();
                    }
                }
                else
                {
                    Disconnect();
                }
            }

            _line = null;
            e.Handled = true;
            _captured = false;
            e.Pointer.Capture(null);
        }
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
        if (_canvas != null && DataContext is SocketViewModel viewModel)
        {
            var socket = viewModel.Model;
            for (int i = _canvas.Children.Count - 1; i >= 0; i--)
            {
                IControl item = _canvas.Children[i];
                if (item is ConnectionLine cline
                    && (cline.First == socket || cline.Second == socket))
                {
                    ISocket? target = cline.First == socket ? cline.Second : cline.First;
                    if (target == null)
                        goto RemoveLine;

                    var args = new SocketConnectRequestedEventArgs(target, State);
                    DisconnectRequested?.Invoke(this, args);
                    if (args.State != SocketState.Disconnected)
                        continue;

                    RemoveLine:
                    _canvas.Children.Remove(cline);
                }
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (State == SocketState.Connected)
        {
            context.FillRectangle(Brush ?? Brushes.Teal, new Rect(0, 0, 10, 10), 5);
        }
        else if (State == SocketState.Invalid)
        {
            context.FillRectangle(_criticalBrush ?? Brushes.OrangeRed, new Rect(0, 0, 10, 10), 5);
        }
        else
        {
            context.FillRectangle(Brushes.Gray, new Rect(0, 0, 10, 10), 5);
        }
    }
}
