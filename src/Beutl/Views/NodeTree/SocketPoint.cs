using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using Beutl.NodeTree;
using Beutl.ViewModels.NodeTree;

using Microsoft.CodeAnalysis.CSharp.Syntax;

using Reactive.Bindings.Extensions;

namespace Beutl.Views.NodeTree;

public class SocketConnectRequestedEventArgs : EventArgs
{
    public SocketConnectRequestedEventArgs(ISocket target, bool isConnected)
    {
        Target = target;
        IsConnected = isConnected;
    }

    public ISocket Target { get; }

    public bool IsConnected { get; set; }
}

public sealed class ConnectionLine : Line
{
    public static readonly StyledProperty<IInputSocket?> InputSocketProperty
        = AvaloniaProperty.Register<ConnectionLine, IInputSocket?>(nameof(InputSocket));
    public static readonly StyledProperty<IOutputSocket?> OutputSocketProperty
        = AvaloniaProperty.Register<ConnectionLine, IOutputSocket?>(nameof(OutputSocket));
    private IDisposable? _strokeBinding;

    static ConnectionLine()
    {
        StrokeThicknessProperty.OverrideDefaultValue<ConnectionLine>(3);
    }

    public IInputSocket? InputSocket
    {
        get => GetValue(InputSocketProperty);
        set => SetValue(InputSocketProperty, value);
    }

    public IOutputSocket? OutputSocket
    {
        get => GetValue(OutputSocketProperty);
        set => SetValue(OutputSocketProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _strokeBinding = this.GetResourceObservable("TextControlForeground")
            .CombineLatest(
                this.GetResourceObservable("SystemFillColorCriticalBrush"),
                this.GetObservable(InputSocketProperty)
                    .SelectMany(o => (o as CoreObject)?.GetObservable(NodeItem.IsValidProperty) ?? Observable.Return<bool?>(null)))
            .ObserveOnUIDispatcher()
            .Subscribe(x =>
            {
                if (x.Third == false)
                {
                    Stroke = x.Second as IBrush;
                }
                else
                {
                    Stroke = x.First as IBrush;
                }
            });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _strokeBinding?.Dispose();
    }

    public bool Match(ISocket? first, ISocket? second)
    {
        return (InputSocket == first && OutputSocket == second)
            || (OutputSocket == first && InputSocket == second);
    }

    public bool Match(ISocket? socket)
    {
        return InputSocket == socket || OutputSocket == socket;
    }

    public ISocket? GetTarget(ISocket? socket)
    {
        return InputSocket == socket ? OutputSocket
            : OutputSocket == socket ? InputSocket : null;
    }

    public bool SetSocket(ISocket socket)
    {
        if (socket is IInputSocket input && InputSocket == null)
        {
            InputSocket = input;
            return true;
        }
        else if (socket is IOutputSocket output && OutputSocket == null)
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

        using var context = geometry.Open();

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
            _line = new ConnectionLine()
            {
                EndPoint = e.GetPosition(_canvas)
            };
            if (viewModel is InputSocketViewModel)
            {
                _line.Bind(Line.StartPointProperty, viewModel.SocketPosition.ToBinding());
            }
            else
            {
                _line.Bind(Line.EndPointProperty, viewModel.SocketPosition.ToBinding());
            }
            _line.SetSocket(viewModel.Model);
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
                        if (endViewModel is InputSocketViewModel)
                        {
                            _line.Bind(Line.StartPointProperty, endViewModel.SocketPosition.ToBinding());
                        }
                        else
                        {
                            _line.Bind(Line.EndPointProperty, endViewModel.SocketPosition.ToBinding());
                        }

                        if (!_line.SetSocket(endViewModel.Model))
                        {
                            Disconnect();
                            goto Finally;
                        }
                    }

                    var args = new SocketConnectRequestedEventArgs(endViewModel.Model, false);
                    ConnectRequested?.Invoke(this, args);
                    if (!args.IsConnected)
                    {
                        Disconnect();
                    }
                }
                else
                {
                    Disconnect();
                }
            }

        Finally:
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
            ISocket socket = viewModel.Model;
            for (int i = _canvas.Children.Count - 1; i >= 0; i--)
            {
                IControl item = _canvas.Children[i];
                if (item is ConnectionLine cline && cline.Match(socket))
                {
                    ISocket? target = cline.GetTarget(socket);
                    if (target == null)
                        goto RemoveLine;

                    var args = new SocketConnectRequestedEventArgs(target, true);
                    DisconnectRequested?.Invoke(this, args);
                    if (args.IsConnected)
                    {
                        continue;
                    }

                RemoveLine:
                    _canvas.Children.Remove(cline);
                }
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (IsConnected)
        {
            context.FillRectangle(Brush ?? Brushes.Teal, new Rect(0, 0, 10, 10), 5);
        }
        else
        {
            context.FillRectangle(Brushes.Gray, new Rect(0, 0, 10, 10), 5);
        }
    }
}
