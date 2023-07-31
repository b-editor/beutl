using System.ComponentModel.DataAnnotations;

using Beutl.Language;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PixelRectNode : Node
{
    private static readonly CoreProperty<PixelPoint> TopLeftProperty
        = ConfigureProperty<PixelPoint, PixelRectNode>(o => o.TopLeft)
            .DefaultValue(default)
            .Register();
    private static readonly CoreProperty<PixelSize> SizeProperty
        = ConfigureProperty<PixelSize, PixelRectNode>(o => o.Size)
            .DefaultValue(default)
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

    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
    private PixelPoint TopLeft
    {
        get => default;
        set { }
    }

    [Display(Name = nameof(Strings.Size), ResourceType = typeof(Strings))]
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
