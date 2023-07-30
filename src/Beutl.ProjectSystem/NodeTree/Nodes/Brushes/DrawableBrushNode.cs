using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class DrawableBrushNode : TileBrushNode<DrawableBrush>
{
    private readonly InputSocket<Drawable?> _drawableSocket;

    public DrawableBrushNode()
    {
        _drawableSocket = AsInput(DrawableBrush.DrawableProperty);
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new DrawableBrush();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        DrawableBrush brush = context.GetOrSetState<DrawableBrush>();
        base.Evaluate(context);

        brush.Drawable = _drawableSocket.Value;
        OutputSocket.Value = brush;
    }
}
