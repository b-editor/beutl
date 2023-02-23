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

    protected override void EvaluateCore(Drawable drawable, object? state)
    {
        if (state is SkewTransform model
            && drawable.Transform is SpecializedTransformGroup group)
        {
            model.SkewY = _skewXSocket.Value;
            model.SkewY = _skewYSocket.Value;

            group.AcceptTransform(model);
        }
    }

    protected override void Attach(Drawable drawable, object? state)
    {
        if (state is SkewTransform model
            && drawable.Transform is SpecializedTransformGroup group)
        {
            group.Children.Add(model);
        }
    }

    protected override void Detach(Drawable drawable, object? state)
    {
        if (state is SkewTransform model
            && drawable.Transform is SpecializedTransformGroup group)
        {
            group.Children.Remove(model);
        }
    }
}
