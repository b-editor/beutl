using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public sealed class TranslateTransformNode : TransformNode
{
    private readonly InputSocket<float> _xSocket;
    private readonly InputSocket<float> _ySocket;

    public TranslateTransformNode()
    {
        _xSocket = AsInput(TranslateTransform.XProperty).AcceptNumber();
        _ySocket = AsInput(TranslateTransform.YProperty).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new TransformNodeEvaluationState(new TranslateTransform());
    }

    protected override void EvaluateCore(ITransform? state)
    {
        if (state is TranslateTransform model)
        {
            model.X = _xSocket.Value;
            model.Y = _ySocket.Value;
        }
    }
}
