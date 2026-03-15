using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.NodeGraph;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public sealed class ConnectionLine : Line
{
    public static readonly StyledProperty<InputPortViewModel?> InputPortProperty
        = AvaloniaProperty.Register<ConnectionLine, InputPortViewModel?>(nameof(InputPort));
    public static readonly StyledProperty<OutputPortViewModel?> OutputPortProperty
        = AvaloniaProperty.Register<ConnectionLine, OutputPortViewModel?>(nameof(OutputPort));
    public static readonly StyledProperty<ConnectionViewModel?> ConnectionViewModelProperty
        = AvaloniaProperty.Register<ConnectionLine, ConnectionViewModel?>(nameof(ConnectionViewModel));
    private IDisposable? _strokeBinding;

    static ConnectionLine()
    {
        StrokeThicknessProperty.OverrideDefaultValue<ConnectionLine>(3);
    }

    public InputPortViewModel? InputPort
    {
        get => GetValue(InputPortProperty);
        set => SetValue(InputPortProperty, value);
    }

    public OutputPortViewModel? OutputPort
    {
        get => GetValue(OutputPortProperty);
        set => SetValue(OutputPortProperty, value);
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

    public bool Match(NodePortViewModel? first, NodePortViewModel? second)
    {
        return (InputPort == first && OutputPort == second)
               || (OutputPort == first && InputPort == second);
    }

    public bool Match(NodePortViewModel? port)
    {
        return InputPort == port || OutputPort == port;
    }

    public NodePortViewModel? GetTarget(NodePortViewModel? port)
    {
        return InputPort == port ? OutputPort
            : OutputPort == port ? InputPort : null;
    }

    public bool Match(Connection connection)
    {
        return ConnectionViewModel?.Connection == connection;
    }

    public bool Match(INodePort? first, INodePort? second)
    {
        return (InputPort?.Model == first && OutputPort?.Model == second)
               || (OutputPort?.Model == first && InputPort?.Model == second);
    }

    public bool Match(INodePort? port)
    {
        return InputPort?.Model == port || OutputPort?.Model == port;
    }

    public INodePort? GetTarget(INodePort? port)
    {
        return InputPort?.Model == port ? OutputPort?.Model
            : OutputPort?.Model == port ? InputPort?.Model : null;
    }

    public bool SetNodePort(NodePortViewModel port)
    {
        if (port is InputPortViewModel input && InputPort == null)
        {
            InputPort = input;
            return true;
        }
        else if (port is OutputPortViewModel output && OutputPort == null)
        {
            OutputPort = output;
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
