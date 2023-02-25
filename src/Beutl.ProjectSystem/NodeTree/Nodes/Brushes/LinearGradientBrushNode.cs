using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class LinearGradientBrushNode : GradientBrushNode<LinearGradientBrush>
{
    private readonly InputSocket<RelativePoint> _startPointSocket;
    private readonly InputSocket<RelativePoint> _endPointSocket;

    public LinearGradientBrushNode()
    {
        _startPointSocket = AsInput(LinearGradientBrush.StartPointProperty);
        _endPointSocket = AsInput(LinearGradientBrush.EndPointProperty);
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new LinearGradientBrush();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        LinearGradientBrush brush = context.GetOrSetState<LinearGradientBrush>();
        base.Evaluate(context);

        brush.StartPoint = _startPointSocket.Value;
        brush.EndPoint = _endPointSocket.Value;
        OutputSocket.Value = brush;
    }
}
