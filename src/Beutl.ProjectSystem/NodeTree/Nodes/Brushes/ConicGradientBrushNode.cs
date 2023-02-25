using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class ConicGradientBrushNode : GradientBrushNode<ConicGradientBrush>
{
    private readonly InputSocket<RelativePoint> _centerSocket;
    private readonly InputSocket<float> _angleSocket;

    public ConicGradientBrushNode()
    {
        _centerSocket = AsInput(ConicGradientBrush.CenterProperty);
        _angleSocket = AsInput(ConicGradientBrush.AngleProperty);
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new ConicGradientBrush();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        ConicGradientBrush brush = context.GetOrSetState<ConicGradientBrush>();
        base.Evaluate(context);

        brush.Center = _centerSocket.Value;
        brush.Angle = _angleSocket.Value;
        OutputSocket.Value = brush;
    }
}
