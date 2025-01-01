using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Geometry;

public sealed class RoundedRectGeometryNode : Node
{
    private readonly OutputSocket<RoundedRectGeometry> _outputSocket;
    private readonly InputSocket<float> _widthSocket;
    private readonly InputSocket<float> _heightSocket;
    private readonly InputSocket<CornerRadius> _radiusSocket;

    public RoundedRectGeometryNode()
    {
        _outputSocket = AsOutput<RoundedRectGeometry>("Geometry");

        _widthSocket = AsInput<float, RoundedRectGeometry>(RoundedRectGeometry.WidthProperty, value: 100).AcceptNumber();
        _heightSocket = AsInput<float, RoundedRectGeometry>(RoundedRectGeometry.HeightProperty, value: 100).AcceptNumber();
        _radiusSocket = AsInput<CornerRadius, RoundedRectGeometry>(RoundedRectGeometry.CornerRadiusProperty, value: new(25));
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new RoundedRectGeometry();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        RoundedRectGeometry rectangle = context.GetOrSetState<RoundedRectGeometry>();
        rectangle.Width = _widthSocket.Value;
        rectangle.Height = _heightSocket.Value;
        rectangle.CornerRadius = _radiusSocket.Value;
        _outputSocket.Value = rectangle;
    }
}
