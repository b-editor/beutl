using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class BrushNode<T> : Node
    where T : Brush.Resource, new()
{
    public BrushNode()
    {
        OutputSocket = AsOutput<T>("Brush");
        OpacitySocket = AsInput<float>("Opacity").AcceptNumber();
        OpacitySocket.Value = 100;
        TransformSocket = AsInput<Matrix>("Transform");
        TransformSocket.RegisterReceiver((obj, out value) =>
        {
            if (obj is Graphics.Transformation.Transform.Resource transform)
            {
                value = transform.Matrix;
                return true;
            }
            else
            {
                value = Matrix.Identity;
                return false;
            }
        });
        TransformOriginSocket = AsInput<RelativePoint>("TransformOrigin");
    }

    protected OutputSocket<T> OutputSocket { get; }

    protected InputSocket<float> OpacitySocket { get; }

    protected InputSocket<Matrix> TransformSocket { get; }

    protected InputSocket<RelativePoint> TransformOriginSocket { get; }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new T();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }


    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        Brush.Resource? brush = context.GetOrDefaultState<Brush.Resource>();
        if (brush == null) return;

        bool changed = false;
        if (brush.Opacity != OpacitySocket.Value)
        {
            brush.Opacity = OpacitySocket.Value;
            changed = true;
        }

        if (brush.Transform is { } matrixTransform)
        {
            if (matrixTransform.Matrix != TransformSocket.Value)
            {
                matrixTransform.Matrix = TransformSocket.Value;
                changed = true;
            }
        }
        else
        {
            brush.Transform = new() { Matrix = TransformSocket.Value };
            changed = true;
        }

        if (brush.TransformOrigin != TransformOriginSocket.Value)
        {
            brush.TransformOrigin = TransformOriginSocket.Value;
            changed = true;
        }

        if (changed)
        {
            brush.Version++;
        }
    }
}
