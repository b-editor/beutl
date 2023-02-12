using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class TranslateNode : ConfigureNode
{
    private readonly InputSocket<float> _xSocket;
    private readonly InputSocket<float> _ySocket;
    private readonly TranslateTransform _model = new();

    public TranslateNode()
    {
        _xSocket = AsInput(TranslateTransform.XProperty).AcceptNumber();
        _ySocket = AsInput(TranslateTransform.YProperty).AcceptNumber();
    }

    protected override void EvaluateCore(EvaluationContext context)
    {
        _model.X = _xSocket.Value;
        _model.Y = _ySocket.Value;
    }

    protected override void Attach(Drawable drawable)
    {
        if (drawable.Transform is not TransformGroup group)
        {
            drawable.Transform = group = new TransformGroup();
        }

        group.Children.Add(_model);
    }

    protected override void Detach(Drawable drawable)
    {
        if (drawable.Transform is TransformGroup group)
        {
            group.Children.Remove(_model);
        }
    }
}
