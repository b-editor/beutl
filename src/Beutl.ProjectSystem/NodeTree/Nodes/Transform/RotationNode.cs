using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class RotationNode : ConfigureNode
{
    private readonly InputSocket<float> _rotationSocket;
    private readonly RotationTransform _model = new();

    public RotationNode()
    {
        _rotationSocket = AsInput(RotationTransform.RotationProperty);
    }

    protected override void EvaluateCore(EvaluationContext context)
    {
        _model.Rotation = _rotationSocket.Value;
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
