using System.ComponentModel.DataAnnotations;

using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class RectNode : Node
{
    private static readonly CoreProperty<Point> TopLeftProperty
        = ConfigureProperty<Point, RectNode>(o => o.TopLeft)
            .DefaultValue(default)
            .Register();
    private static readonly CoreProperty<Size> SizeProperty
        = ConfigureProperty<Size, RectNode>(o => o.Size)
            .DefaultValue(default)
            .Register();
    private readonly OutputSocket<Rect> _valueSocket;
    private readonly InputSocket<Point> _positionSocket;
    private readonly InputSocket<Size> _sizeSocket;

    public RectNode()
    {
        _valueSocket = AsOutput<Rect>("Rect");
        _positionSocket = AsInput(TopLeftProperty).AcceptNumber();
        _sizeSocket = AsInput(SizeProperty).AcceptNumber();
    }

    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
    private Point TopLeft
    {
        get => default;
        set { }
    }

    [Display(Name = nameof(Strings.Size), ResourceType = typeof(Strings))]
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
