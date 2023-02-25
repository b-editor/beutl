using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class ScaleNode : ConfigureNode
{
    private readonly InputSocket<float> _scaleSocket;
    private readonly InputSocket<float> _scaleXSocket;
    private readonly InputSocket<float> _scaleYSocket;

    public ScaleNode()
    {
        _scaleSocket = AsInput(ScaleTransform.ScaleProperty).AcceptNumber();
        _scaleXSocket = AsInput(ScaleTransform.ScaleXProperty).AcceptNumber();
        _scaleYSocket = AsInput(ScaleTransform.ScaleYProperty).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new ConfigureNodeEvaluationState(null, new ScaleTransform());
    }

    protected override void EvaluateCore(Drawable drawable, object? state)
    {
        if (state is ScaleTransform model
            && drawable.Transform is TransformGroup group)
        {
            model.Scale = _scaleSocket.Value;
            model.ScaleX = _scaleXSocket.Value;
            model.ScaleY = _scaleYSocket.Value;

            group.Children.Add(model);
        }
    }
}
