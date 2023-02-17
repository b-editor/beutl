using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class RelativePointNode : Node
{
    private static readonly CoreProperty<RelativeUnit> UnitProperty
        = ConfigureProperty<RelativeUnit, RelativePointNode>(o => o.Unit)
            .DefaultValue(RelativeUnit.Relative)
            .SerializeName("unit")
            .Register();
    private static readonly CoreProperty<float> XProperty
        = ConfigureProperty<float, RelativePointNode>(o => o.X)
            .DefaultValue(0)
            .SerializeName("x")
            .Register();
    private static readonly CoreProperty<float> YProperty
        = ConfigureProperty<float, RelativePointNode>(o => o.Y)
            .DefaultValue(0)
            .SerializeName("y")
            .Register();
    private readonly OutputSocket<RelativePoint> _valueSocket;
    private readonly NodeItem<RelativeUnit> _unitSocket;
    private readonly InputSocket<float> _xSocket;
    private readonly InputSocket<float> _ySocket;

    public RelativePointNode()
    {
        _valueSocket = AsOutput("RelativePoint", RelativePoint.TopLeft);
        _unitSocket = AsProperty(UnitProperty);
        _xSocket = AsInput(XProperty).AcceptNumber();
        _ySocket = AsInput(YProperty).AcceptNumber();
    }

    private RelativeUnit Unit
    {
        get => RelativeUnit.Relative;
        set { }
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
        _valueSocket.Value = new RelativePoint(_xSocket.Value, _ySocket.Value, _unitSocket.Value);
    }
}
