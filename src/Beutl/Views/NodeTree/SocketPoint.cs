using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;

using Beutl.NodeTree;
using Beutl.ViewModels.NodeTree;

using FluentAvalonia.UI.Media;

using Reactive.Bindings.Extensions;

namespace Beutl.Views.NodeTree;

public class SocketConnectRequestedEventArgs(SocketViewModel target, bool isConnected) : EventArgs
{
    public SocketViewModel Target { get; } = target;

    public bool IsConnected { get; set; } = isConnected;
}

public sealed class ConnectionLine : Line
{
    public static readonly StyledProperty<InputSocketViewModel?> InputSocketProperty
        = AvaloniaProperty.Register<ConnectionLine, InputSocketViewModel?>(nameof(InputSocket));
    public static readonly StyledProperty<OutputSocketViewModel?> OutputSocketProperty
        = AvaloniaProperty.Register<ConnectionLine, OutputSocketViewModel?>(nameof(OutputSocket));
    private IDisposable? _strokeBinding;

    static ConnectionLine()
    {
        StrokeThicknessProperty.OverrideDefaultValue<ConnectionLine>(3);
    }

    public InputSocketViewModel? InputSocket
    {
        get => GetValue(InputSocketProperty);
        set => SetValue(InputSocketProperty, value);
    }

    public OutputSocketViewModel? OutputSocket
    {
        get => GetValue(OutputSocketProperty);
        set => SetValue(OutputSocketProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _strokeBinding = this.GetResourceObservable("TextControlForeground")
            .CombineLatest(
                this.GetResourceObservable("SystemFillColorSuccessBrush"),
                this.GetResourceObservable("SystemFillColorCautionBrush"),
                this.GetResourceObservable("SystemFillColorCriticalBrush"),
                this.GetObservable(InputSocketProperty)
                    .Select(o => o?.Status ?? Observable.Return(ConnectionStatus.Disconnected))
                    .Switch())
            .ObserveOnUIDispatcher()
            .Subscribe(x =>
            {
                switch (x.Fifth)
                {
                    case ConnectionStatus.Disconnected:
                        Stroke = x.First as IBrush;
                        break;
                    case ConnectionStatus.Connected:
                    case ConnectionStatus.Success:
                        Stroke = x.Second as IBrush;
                        break;
                    case ConnectionStatus.Convert:
                        Stroke = x.Third as IBrush;
                        break;
                    case ConnectionStatus.Error:
                        Stroke = x.Fourth as IBrush;
                        break;
                }
            });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _strokeBinding?.Dispose();
    }

    public bool Match(SocketViewModel? first, SocketViewModel? second)
    {
        return (InputSocket == first && OutputSocket == second)
            || (OutputSocket == first && InputSocket == second);
    }

    public bool Match(SocketViewModel? socket)
    {
        return InputSocket == socket || OutputSocket == socket;
    }

    public SocketViewModel? GetTarget(SocketViewModel? socket)
    {
        return InputSocket == socket ? OutputSocket
            : OutputSocket == socket ? InputSocket : null;
    }

    public bool Match(ISocket? first, ISocket? second)
    {
        return (InputSocket?.Model == first && OutputSocket?.Model == second)
            || (OutputSocket?.Model == first && InputSocket?.Model == second);
    }

    public bool Match(ISocket? socket)
    {
        return InputSocket?.Model == socket || OutputSocket?.Model == socket;
    }

    public ISocket? GetTarget(ISocket? socket)
    {
        return InputSocket?.Model == socket ? OutputSocket?.Model
            : OutputSocket?.Model == socket ? InputSocket?.Model : null;
    }

    public bool SetSocket(SocketViewModel socket)
    {
        if (socket is InputSocketViewModel input && InputSocket == null)
        {
            InputSocket = input;
            return true;
        }
        else if (socket is OutputSocketViewModel output && OutputSocket == null)
        {
            OutputSocket = output;
            return true;
        }
        else
        {
            return false;
        }
    }

    protected override Geometry CreateDefiningGeometry()
    {
        var geometry = new StreamGeometry();

        using StreamGeometryContext context = geometry.Open();

        context.BeginFigure(StartPoint, false);

        double delta = 0;
        if (StartPoint.X < EndPoint.X)
        {
            delta = (EndPoint.X - StartPoint.X) / 2;
        }
        else
        {
            delta = (StartPoint.X - EndPoint.X) / 2;
        }

        delta = Math.Clamp(delta, 10, 50);

        context.CubicBezierTo(StartPoint.WithX(StartPoint.X - delta), EndPoint.WithX(EndPoint.X + delta), EndPoint);

        context.EndFigure(false);

        return geometry;
    }
}

public sealed class SocketPoint : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<SocketPoint, IBrush?>(nameof(Brush));

    public static readonly StyledProperty<bool> IsConnectedProperty =
        AvaloniaProperty.Register<SocketPoint, bool>(nameof(IsConnected));

    private ConnectionLine? _line;
    private bool _captured;
    private bool _doubleClick;
    private Canvas? _canvas;

    static SocketPoint()
    {
        AffectsRender<SocketPoint>(BrushProperty, IsConnectedProperty);
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

    public event EventHandler<SocketConnectRequestedEventArgs>? ConnectRequested;

    public event EventHandler<SocketConnectRequestedEventArgs>? DisconnectRequested;

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

        if (e.ClickCount == 2)
        {
            _doubleClick = true;
            e.Handled = true;
        }
        else if (_canvas != null && DataContext is SocketViewModel viewModel)
        {
            PointerPoint point = e.GetCurrentPoint(_canvas);
            if (point.Properties.IsLeftButtonPressed)
            {
                _line = new ConnectionLine();
                if (viewModel is InputSocketViewModel)
                {
                    _line.EndPoint = point.Position;
                    _line.Bind(Line.StartPointProperty, viewModel.SocketPosition.ToBinding());
                }
                else
                {
                    _line.StartPoint = point.Position;
                    _line.Bind(Line.EndPointProperty, viewModel.SocketPosition.ToBinding());
                }
                _line.SetSocket(viewModel);
                _canvas.Children.Insert(0, _line);

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
            if (DataContext is InputSocketViewModel)
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
            if (elm != this && elm is SocketPoint { DataContext: SocketViewModel endViewModel })
            {
                ISocket? socket = endViewModel.Model;
                var args = new SocketConnectRequestedEventArgs(endViewModel, false);
                ConnectRequested?.Invoke(this, args);
                if (args.IsConnected && _line != null)
                {
                    if (endViewModel is InputSocketViewModel)
                    {
                        _line.Bind(Line.StartPointProperty, endViewModel.SocketPosition.ToBinding());
                    }
                    else
                    {
                        _line.Bind(Line.EndPointProperty, endViewModel.SocketPosition.ToBinding());
                    }

                    if (_line.SetSocket(endViewModel))
                    {
                        return true;
                    }
                }
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
            if (!TryConnect(e))
            {
                Disconnect();
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
            for (int i = _canvas.Children.Count - 1; i >= 0; i--)
            {
                Control item = _canvas.Children[i];
                if (item is ConnectionLine cline && cline.Match(viewModel))
                {
                    SocketViewModel? target = cline.GetTarget(viewModel);
                    if (target != null)
                    {
                        var args = new SocketConnectRequestedEventArgs(target, true);
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
