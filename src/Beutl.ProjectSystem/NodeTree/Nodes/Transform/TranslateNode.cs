using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class TranslateNode : ConfigureNode
{
    private readonly InputSocket<float> _xSocket;
    private readonly InputSocket<float> _ySocket;

    public TranslateNode()
    {
        _xSocket = AsInput(TranslateTransform.XProperty).AcceptNumber();
        _ySocket = AsInput(TranslateTransform.YProperty).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new ConfigureNodeEvaluationState(null, new TranslateTransform());
    }

    protected override void EvaluateCore(Drawable drawable, object? state)
    {
        if (state is TranslateTransform model
            && drawable.Transform is TransformGroup group)
        {
            model.X = _xSocket.Value;
            model.Y = _ySocket.Value;
            group.Children.Add(model);
        }
    }
}
