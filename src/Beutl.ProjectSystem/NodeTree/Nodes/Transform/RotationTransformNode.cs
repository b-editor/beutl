using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public sealed class RotationTransformNode : TransformNode
{
    private readonly InputSocket<float> _rotationSocket;

    public RotationTransformNode()
    {
        _rotationSocket = AsInput(RotationTransform.RotationProperty).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new TransformNodeEvaluationState(null, new RotationTransform());
    }

    protected override void EvaluateCore(TransformGroup group, object? state)
    {
        if (state is RotationTransform model)
        {
            model.Rotation = _rotationSocket.Value;

            group.Children.Add(model);
        }
    }
}
