using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class RotationNode : ConfigureNode
{
    private readonly InputSocket<float> _rotationSocket;

    public RotationNode()
    {
        _rotationSocket = AsInput(RotationTransform.RotationProperty).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new ConfigureNodeEvaluationState(null, new RotationTransform());
    }

    protected override void EvaluateCore(NodeEvaluationContext context)
    {
        if (context.State is ConfigureNodeEvaluationState { AddtionalState: RotationTransform model })
        {
            model.Rotation = _rotationSocket.Value;
        }
    }

    protected override void Attach(Drawable drawable, object? state)
    {
        if (state is RotationTransform model)
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
        if (state is RotationTransform model
            && drawable.Transform is TransformGroup group)
        {
            group.Children.Remove(model);
        }
    }
}
