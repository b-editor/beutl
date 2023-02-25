using Beutl.Language;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PixelRectNode : Node
{
    private static readonly CoreProperty<PixelPoint> TopLeftProperty
        = ConfigureProperty<PixelPoint, PixelRectNode>(o => o.TopLeft)
            .Display(Strings.Position)
            .DefaultValue(default)
            .SerializeName("top-left")
            .Register();
    private static readonly CoreProperty<PixelSize> SizeProperty
        = ConfigureProperty<PixelSize, PixelRectNode>(o => o.Size)
            .Display(Strings.Size)
            .DefaultValue(default)
            .SerializeName("size")
            .Register();
    private readonly OutputSocket<PixelRect> _valueSocket;
    private readonly InputSocket<PixelPoint> _positionSocket;
    private readonly InputSocket<PixelSize> _sizeSocket;

    public PixelRectNode()
    {
        _valueSocket = AsOutput<PixelRect>("PixelRect");
        _positionSocket = AsInput(TopLeftProperty).AcceptNumber();
        _sizeSocket = AsInput(SizeProperty).AcceptNumber();
    }

    private PixelPoint TopLeft
    {
        get => default;
        set { }
    }

    private PixelSize Size
    {
        get => default;
        set { }
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        _valueSocket.Value = new PixelRect(_positionSocket.Value, _sizeSocket.Value);
    }
}
