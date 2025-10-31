using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class SolidColorBrushNode : BrushNode<SolidColorBrush.Resource>
{
    private readonly InputSocket<Color> _colorSocket;

    public SolidColorBrushNode()
    {
        _colorSocket = AsInput<Color>("Color");
        Items.RemoveRange(2, 2);
    }
    public override void Evaluate(NodeEvaluationContext context)
    {
        SolidColorBrush.Resource brush = context.GetOrSetState<SolidColorBrush.Resource>();
        base.Evaluate(context);

        if (brush.Color != _colorSocket.Value)
        {
            brush.Color = _colorSocket.Value;
            brush.Version++;
        }

        OutputSocket.Value = brush;
    }
}
