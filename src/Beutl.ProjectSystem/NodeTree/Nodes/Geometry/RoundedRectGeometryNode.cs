using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Geometry;

public sealed class RoundedRectGeometryNode : GeometryNode<RoundedRectGeometry, RoundedRectGeometry.Resource>
{
    public RoundedRectGeometryNode()
    {
        WidthSocket = AsInput<float>("Width").AcceptNumber();
        WidthSocket.Value = 100;
        HeightSocket = AsInput<float>("Height").AcceptNumber();
        HeightSocket.Value = 100;
        RadiusSocket = AsInput<CornerRadius>("Radius");
        RadiusSocket.Value = new CornerRadius(25);
    }

    public InputSocket<float> WidthSocket { get; }

    public InputSocket<float> HeightSocket { get; }

    public InputSocket<CornerRadius> RadiusSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        Object.Width.CurrentValue = WidthSocket.Value;
        Object.Height.CurrentValue = HeightSocket.Value;
        Object.CornerRadius.CurrentValue = RadiusSocket.Value;

        base.Evaluate(context);
    }
}
