using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Immutable;

namespace Beutl.NodeTree.Nodes.Brushes;

public class BrushNode<T> : Node
    where T : Brush, new()
{
    public BrushNode()
    {
        OutputSocket = AsOutput<T>("Output", "Brush");
        OpacitySocket = AsInput(Brush.OpacityProperty).AcceptNumber();
        TransformSocket = AsInput(Brush.TransformProperty);
        TransformSocket.RegisterReceiver((object? obj, out ITransform? value) =>
        {
            if (obj is Matrix mat)
            {
                value = new ImmutableTransform(mat);
                return true;
            }
            else
            {
                value = null;
                return false;
            }
        });

        TransformOriginSocket = AsInput(Brush.TransformOriginProperty);
    }

    protected OutputSocket<T> OutputSocket { get; }

    protected InputSocket<float> OpacitySocket { get; }

    protected InputSocket<ITransform?> TransformSocket { get; }

    protected InputSocket<RelativePoint> TransformOriginSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        Brush? brush = context.GetOrDefaultState<Brush>();
        if (brush != null)
        {
            brush.Opacity = OpacitySocket.Value;
            brush.Transform = TransformSocket.Value;
            brush.TransformOrigin = TransformOriginSocket.Value;
        }
    }
}
