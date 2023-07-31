using System.ComponentModel.DataAnnotations;

using Beutl.Language;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public class PixelSizeNode : Node
{
    private static readonly CoreProperty<int> WidthProperty
        = ConfigureProperty<int, PixelSizeNode>(o => o.Width)
            .DefaultValue(0)
            .Register();
    private static readonly CoreProperty<int> HeightProperty
        = ConfigureProperty<int, PixelSizeNode>(o => o.Height)
            .DefaultValue(0)
            .Register();
    private readonly OutputSocket<PixelSize> _valueSocket;
    private readonly InputSocket<int> _widthSocket;
    private readonly InputSocket<int> _heightSocket;

    public PixelSizeNode()
    {
        _valueSocket = AsOutput<PixelSize>("PixelSize");
        _widthSocket = AsInput(WidthProperty).AcceptNumber();
        _heightSocket = AsInput(HeightProperty).AcceptNumber();
    }

    [Display(Name = nameof(Strings.Width), ResourceType = typeof(Strings))]
    private int Width
    {
        get => 0;
        set { }
    }

    [Display(Name = nameof(Strings.Height), ResourceType = typeof(Strings))]
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
