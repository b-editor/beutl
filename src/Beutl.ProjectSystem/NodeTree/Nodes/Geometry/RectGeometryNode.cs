using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Geometry;

public sealed class RectGeometryNode : GeometryNode<RectGeometry, RectGeometry.Resource>
{
    public RectGeometryNode()
    {
        WidthSocket = AsInput<float>("Width").AcceptNumber();
        WidthSocket.Value = 100;
        HeightSocket = AsInput<float>("Height").AcceptNumber();
        HeightSocket.Value = 100;
    }

    public InputSocket<float> WidthSocket { get; }

    public InputSocket<float> HeightSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        Object.Width.CurrentValue = WidthSocket.Value;
        Object.Height.CurrentValue = HeightSocket.Value;

        base.Evaluate(context);
    }
}
