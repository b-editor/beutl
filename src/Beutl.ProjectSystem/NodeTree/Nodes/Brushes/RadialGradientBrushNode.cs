using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class RadialGradientBrushNode : GradientBrushNode<RadialGradientBrush>
{
    private readonly InputSocket<RelativePoint> _centerSocket;
    private readonly InputSocket<RelativePoint> _originSocket;
    private readonly InputSocket<float> _radiusSocket;

    public RadialGradientBrushNode()
    {
        _centerSocket = AsInput(RadialGradientBrush.CenterProperty);
        _originSocket = AsInput(RadialGradientBrush.GradientOriginProperty);
        _radiusSocket = AsInput(RadialGradientBrush.RadiusProperty);
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new RadialGradientBrush();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        RadialGradientBrush brush = context.GetOrSetState<RadialGradientBrush>();
        base.Evaluate(context);

        brush.Center = _centerSocket.Value;
        brush.GradientOrigin = _originSocket.Value;
        brush.Radius = _radiusSocket.Value;
        OutputSocket.Value = brush;
    }
}
