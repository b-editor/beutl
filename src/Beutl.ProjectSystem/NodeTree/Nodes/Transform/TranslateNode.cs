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

    protected override void EvaluateCore(NodeEvaluationContext context)
    {
        if (context.State is ConfigureNodeEvaluationState { AddtionalState: TranslateTransform model })
        {
            model.X = _xSocket.Value;
            model.Y = _ySocket.Value;
        }
    }

    protected override void Attach(Drawable drawable, object? state)
    {
        if (state is TranslateTransform model)
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
        if (state is TranslateTransform model
            && drawable.Transform is TransformGroup group)
        {
            group.Children.Remove(model);
        }
    }
}
