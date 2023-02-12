using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class RectNode : Node
{
    private static readonly CoreProperty<Point> TopLeftProperty
        = ConfigureProperty<Point, RectNode>(o => o.TopLeft)
            .Display(Strings.Position)
            .DefaultValue(default)
            .SerializeName("top-left")
            .Register();
    private static readonly CoreProperty<Size> SizeProperty
        = ConfigureProperty<Size, RectNode>(o => o.Size)
            .Display(Strings.Size)
            .DefaultValue(default)
            .SerializeName("size")
            .Register();
    private readonly OutputSocket<Rect> _valueSocket;
    private readonly InputSocket<Point> _positionSocket;
    private readonly InputSocket<Size> _sizeSocket;

    public RectNode()
    {
        _valueSocket = AsOutput<Rect>("Output", "PixelRect");
        _positionSocket = AsInput(TopLeftProperty).AcceptNumber();
        _sizeSocket = AsInput(SizeProperty).AcceptNumber();
    }

    private Point TopLeft
    {
        get => default;
        set { }
    }

    private Size Size
    {
        get => default;
        set { }
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new Rect(_positionSocket.Value, _sizeSocket.Value);
    }
}
