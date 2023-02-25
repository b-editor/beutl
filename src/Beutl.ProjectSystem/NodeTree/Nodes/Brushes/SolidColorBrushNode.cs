using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class SolidColorBrushNode : BrushNode<SolidColorBrush>
{
    private readonly InputSocket<Color> _colorSocket;

    public SolidColorBrushNode()
    {
        _colorSocket = AsInput(SolidColorBrush.ColorProperty);
        Items.RemoveRange(2, 2);
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new SolidColorBrush();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        SolidColorBrush brush = context.GetOrSetState<SolidColorBrush>();
        base.Evaluate(context);

        brush.Color = _colorSocket.Value;
        OutputSocket.Value = brush;
    }
}
