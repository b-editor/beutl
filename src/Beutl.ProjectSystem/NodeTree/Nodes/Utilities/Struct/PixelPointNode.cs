using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PixelPointNode : Node
{
    private static readonly CoreProperty<int> XProperty
        = ConfigureProperty<int, PixelPointNode>(o => o.X)
            .DefaultValue(0)
            .Register();
    private static readonly CoreProperty<int> YProperty
        = ConfigureProperty<int, PixelPointNode>(o => o.Y)
            .DefaultValue(0)
            .Register();
    private readonly OutputSocket<PixelPoint> _valueSocket;
    private readonly InputSocket<int> _xSocket;
    private readonly InputSocket<int> _ySocket;

    public PixelPointNode()
    {
        _valueSocket = AsOutput<PixelPoint>("PixelPoint");
        _xSocket = AsInput(XProperty).AcceptNumber();
        _ySocket = AsInput(YProperty).AcceptNumber();
    }

    private int X
    {
        get => 0;
        set { }
    }

    private int Y
    {
        get => 0;
        set { }
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new PixelPoint(_xSocket.Value, _ySocket.Value);
    }
}
