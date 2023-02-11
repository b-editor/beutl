using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class ScaleNode : ConfigureNode
{
    private readonly InputSocket<float> _scaleSocket;
    private readonly InputSocket<float> _scaleXSocket;
    private readonly InputSocket<float> _scaleYSocket;
    private readonly ScaleTransform _model = new();

    public ScaleNode()
    {
        _scaleSocket = AsInput(ScaleTransform.ScaleProperty);
        _scaleXSocket = AsInput(ScaleTransform.ScaleXProperty);
        _scaleYSocket = AsInput(ScaleTransform.ScaleYProperty);
    }

    protected override void EvaluateCore(EvaluationContext context)
    {
        _model.Scale = _scaleSocket.Value;
        _model.ScaleX = _scaleXSocket.Value;
        _model.ScaleY = _scaleYSocket.Value;
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
