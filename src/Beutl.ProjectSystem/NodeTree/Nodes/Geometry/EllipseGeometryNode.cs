using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Geometry;

public sealed class EllipseGeometryNode : Node
{
    private readonly OutputSocket<EllipseGeometry> _outputSocket;
    private readonly InputSocket<float> _widthSocket;
    private readonly InputSocket<float> _heightSocket;

    public EllipseGeometryNode()
    {
        _outputSocket = AsOutput<EllipseGeometry>("Geometry");

        _widthSocket = AsInput<float, EllipseGeometry>(EllipseGeometry.WidthProperty, value: 100).AcceptNumber();
        _heightSocket = AsInput<float, EllipseGeometry>(EllipseGeometry.HeightProperty, value: 100).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new EllipseGeometry();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        EllipseGeometry ellipse = context.GetOrSetState<EllipseGeometry>();
        ellipse.Width = _widthSocket.Value;
        ellipse.Height = _heightSocket.Value;
        _outputSocket.Value = ellipse;
    }
}
