using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class SizeNode : Node
{
    private static readonly CoreProperty<float> WidthProperty
        = ConfigureProperty<float, SizeNode>(o => o.Width)
            .Display(Strings.Width)
            .DefaultValue(0)
            .SerializeName("width")
            .Register();
    private static readonly CoreProperty<float> HeightProperty
        = ConfigureProperty<float, SizeNode>(o => o.Height)
            .Display(Strings.Height)
            .DefaultValue(0)
            .SerializeName("height")
            .Register();
    private readonly OutputSocket<Size> _valueSocket;
    private readonly InputSocket<float> _widthSocket;
    private readonly InputSocket<float> _heightSocket;

    public SizeNode()
    {
        _valueSocket = AsOutput<Size>("Output", "Size");
        _widthSocket = AsInput(WidthProperty).AcceptNumber();
        _heightSocket = AsInput(HeightProperty).AcceptNumber();
    }

    private float Width
    {
        get => 0;
        set { }
    }

    private float Height
    {
        get => 0;
        set { }
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new Size(_widthSocket.Value, _heightSocket.Value);
    }
}
