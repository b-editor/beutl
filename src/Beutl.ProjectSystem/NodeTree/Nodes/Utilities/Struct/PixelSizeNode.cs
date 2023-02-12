using Beutl.Language;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PixelSizeNode : Node
{
    private static readonly CoreProperty<int> WidthProperty
        = ConfigureProperty<int, PixelSizeNode>(o => o.Width)
            .Display(Strings.Width)
            .DefaultValue(0)
            .SerializeName("width")
            .Register();
    private static readonly CoreProperty<int> HeightProperty
        = ConfigureProperty<int, PixelSizeNode>(o => o.Height)
            .Display(Strings.Height)
            .DefaultValue(0)
            .SerializeName("height")
            .Register();
    private readonly OutputSocket<PixelSize> _valueSocket;
    private readonly InputSocket<int> _widthSocket;
    private readonly InputSocket<int> _heightSocket;

    public PixelSizeNode()
    {
        _valueSocket = AsOutput<PixelSize>("Output", "PixelSize");
        _widthSocket = AsInput(WidthProperty).AcceptNumber();
        _heightSocket = AsInput(HeightProperty).AcceptNumber();
    }

    private int Width
    {
        get => 0;
        set { }
    }

    private int Height
    {
        get => 0;
        set { }
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new PixelSize(_widthSocket.Value, _heightSocket.Value);
    }
}
