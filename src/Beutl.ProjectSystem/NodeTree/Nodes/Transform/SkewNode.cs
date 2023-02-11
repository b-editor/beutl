using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class SkewNode : ConfigureNode
{
    private readonly InputSocket<float> _skewXSocket;
    private readonly InputSocket<float> _skewYSocket;
    private readonly SkewTransform _model = new();

    public SkewNode()
    {
        _skewXSocket = AsInput(SkewTransform.SkewXProperty);
        _skewYSocket = AsInput(SkewTransform.SkewYProperty);
    }

    protected override void EvaluateCore(EvaluationContext context)
    {
        _model.SkewY = _skewXSocket.Value;
        _model.SkewY = _skewYSocket.Value;
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
