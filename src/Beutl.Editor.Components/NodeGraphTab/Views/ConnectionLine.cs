using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.NodeGraph;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public sealed partial class ConnectionLine : Line
{
    private IDisposable? _strokeBinding;

    static ConnectionLine()
    {
        StrokeThicknessProperty.OverrideDefaultValue<ConnectionLine>(3);
    }

    [GeneratedStyledProperty]
    public partial InputPortViewModel? InputPort { get; set; }

    [GeneratedStyledProperty]
    public partial OutputPortViewModel? OutputPort { get; set; }

    [GeneratedStyledProperty]
    public partial ConnectionViewModel? ConnectionViewModel { get; set; }

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
                if (ConnectionViewModel?.HasFallback == true)
                {
                    Stroke = x.Third as IBrush;
                    return;
                }

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

    public bool Match(NodePortViewModel? port)
    {
        return InputPort == port || OutputPort == port;
    }

    public NodePortViewModel? GetTarget(NodePortViewModel? port)
    {
        return InputPort == port ? OutputPort
            : OutputPort == port ? InputPort : null;
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

        double delta = Math.Clamp(Math.Abs(EndPoint.X - StartPoint.X) / 2, 10, 50);

        context.CubicBezierTo(StartPoint.WithX(StartPoint.X - delta), EndPoint.WithX(EndPoint.X + delta), EndPoint);

        context.EndFigure(false);

        return geometry;
    }
}
