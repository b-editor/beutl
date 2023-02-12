using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class SkewNode : ConfigureNode
{
    private readonly InputSocket<float> _skewXSocket;
    private readonly InputSocket<float> _skewYSocket;

    public SkewNode()
    {
        _skewXSocket = AsInput(SkewTransform.SkewXProperty).AcceptNumber();
        _skewYSocket = AsInput(SkewTransform.SkewYProperty).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new ConfigureNodeEvaluationState(null, new SkewTransform());
    }

    protected override void EvaluateCore(NodeEvaluationContext context)
    {
        if (context.State is ConfigureNodeEvaluationState { AddtionalState: SkewTransform model })
        {
            model.SkewY = _skewXSocket.Value;
            model.SkewY = _skewYSocket.Value;
        }
    }

    protected override void Attach(Drawable drawable, object? state)
    {
        if (state is SkewTransform model)
        {
            if (drawable.Transform is not TransformGroup group)
            {
                drawable.Transform = group = new TransformGroup();
            }

            group.Children.Add(model);
        }
    }

    protected override void Detach(Drawable drawable, object? state)
    {
        if (state is SkewTransform model
            && drawable.Transform is TransformGroup group)
        {
            group.Children.Remove(model);
        }
    }
}
