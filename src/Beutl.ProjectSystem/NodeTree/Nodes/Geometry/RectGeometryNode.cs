using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Geometry;

public sealed class RectGeometryNode : Node
{
    private readonly OutputSocket<RectGeometry> _outputSocket;
    private readonly InputSocket<float> _widthSocket;
    private readonly InputSocket<float> _heightSocket;

    public RectGeometryNode()
    {
        _outputSocket = AsOutput<RectGeometry>("Geometry");

        _widthSocket = AsInput<float, RectGeometry>(RectGeometry.WidthProperty, value: 100).AcceptNumber();
        _heightSocket = AsInput<float, RectGeometry>(RectGeometry.HeightProperty, value: 100).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new RectGeometry();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        RectGeometry rectangle = context.GetOrSetState<RectGeometry>();
        while (rectangle.BatchUpdate)
        {
            rectangle.EndBatchUpdate();
        }

        rectangle.BeginBatchUpdate();
        rectangle.Width = _widthSocket.Value;
        rectangle.Height = _heightSocket.Value;
        _outputSocket.Value = rectangle;
    }
}
