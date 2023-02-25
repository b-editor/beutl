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

    protected override void EvaluateCore(Drawable drawable, object? state)
    {
        if (state is RotationTransform model
            && drawable.Transform is TransformGroup group)
        {
            model.Rotation = _rotationSocket.Value;

            group.Children.Add(model);
        }
    }
}
