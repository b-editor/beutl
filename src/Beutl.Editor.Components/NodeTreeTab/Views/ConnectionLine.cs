using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Beutl.Editor.Components.NodeTreeTab.ViewModels;
using Beutl.NodeTree;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.NodeTreeTab.Views;

public sealed class ConnectionLine : Line
{
    public static readonly StyledProperty<InputSocketViewModel?> InputSocketProperty
        = AvaloniaProperty.Register<ConnectionLine, InputSocketViewModel?>(nameof(InputSocket));
    public static readonly StyledProperty<OutputSocketViewModel?> OutputSocketProperty
        = AvaloniaProperty.Register<ConnectionLine, OutputSocketViewModel?>(nameof(OutputSocket));
    public static readonly StyledProperty<ConnectionViewModel?> ConnectionViewModelProperty
        = AvaloniaProperty.Register<ConnectionLine, ConnectionViewModel?>(nameof(ConnectionViewModel));
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

    public ConnectionViewModel? ConnectionViewModel
    {
        get => GetValue(ConnectionViewModelProperty);
        set => SetValue(ConnectionViewModelProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _strokeBinding = this.GetResourceObservable("TextControlForeground")
            .CombineLatest(
                this.GetResourceObservable("SystemFillColorSuccessBrush"),
                this.GetResourceObservable("SystemFillColorCautionBrush"),
                this.GetResourceObservable("SystemFillColorCriticalBrush"),
                this.GetObservable(ConnectionViewModelProperty)
                    .Select(o => o?.Status as IObservable<ConnectionStatus> ?? Observable.ReturnThenNever(ConnectionStatus.Disconnected))
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

    public bool Match(Connection connection)
    {
        return ConnectionViewModel?.Connection == connection;
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
