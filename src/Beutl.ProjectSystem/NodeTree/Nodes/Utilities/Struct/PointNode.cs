using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PointNode : Node
{
    private static readonly CoreProperty<float> XProperty
        = ConfigureProperty<float, PointNode>(o => o.X)
            .DefaultValue(0)
            .SerializeName("x")
            .Register();
    private static readonly CoreProperty<float> YProperty
        = ConfigureProperty<float, PointNode>(o => o.Y)
            .DefaultValue(0)
            .SerializeName("y")
            .Register();
    private readonly OutputSocket<Point> _valueSocket;
    private readonly InputSocket<float> _xSocket;
    private readonly InputSocket<float> _ySocket;

    public PointNode()
    {
        _valueSocket = AsOutput<Point>("Output", "Point");
        _xSocket = AsInput(XProperty).AcceptNumber();
        _ySocket = AsInput(YProperty).AcceptNumber();
    }

    private float X
    {
        get => 0;
        set { }
    }

    private float Y
    {
        get => 0;
        set { }
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new Point(_xSocket.Value, _ySocket.Value);
    }
}
