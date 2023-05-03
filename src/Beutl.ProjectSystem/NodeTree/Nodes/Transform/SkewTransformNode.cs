using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public sealed class SkewTransformNode : TransformNode
{
    private readonly InputSocket<float> _skewXSocket;
    private readonly InputSocket<float> _skewYSocket;

    public SkewTransformNode()
    {
        _skewXSocket = AsInput(SkewTransform.SkewXProperty).AcceptNumber();
        _skewYSocket = AsInput(SkewTransform.SkewYProperty).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new TransformNodeEvaluationState(new SkewTransform());
    }

    protected override void EvaluateCore(ITransform? state)
    {
        if (state is SkewTransform model)
        {
            model.SkewY = _skewXSocket.Value;
            model.SkewY = _skewYSocket.Value;
        }
    }
}
