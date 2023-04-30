using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public sealed class ScaleTransformNode : TransformNode
{
    private readonly InputSocket<float> _scaleSocket;
    private readonly InputSocket<float> _scaleXSocket;
    private readonly InputSocket<float> _scaleYSocket;

    public ScaleTransformNode()
    {
        _scaleSocket = AsInput(ScaleTransform.ScaleProperty).AcceptNumber();
        _scaleXSocket = AsInput(ScaleTransform.ScaleXProperty).AcceptNumber();
        _scaleYSocket = AsInput(ScaleTransform.ScaleYProperty).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new TransformNodeEvaluationState(null, new ScaleTransform());
    }

    protected override void EvaluateCore(TransformGroup group, object? state)
    {
        if (state is ScaleTransform model)
        {
            model.Scale = _scaleSocket.Value;
            model.ScaleX = _scaleXSocket.Value;
            model.ScaleY = _scaleYSocket.Value;

            group.Children.Add(model);
        }
    }
}
