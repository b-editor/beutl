using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class Rotation3DNode : ConfigureNode
{
    private readonly InputSocket<float> _rotationXSocket;
    private readonly InputSocket<float> _rotationYSocket;
    private readonly InputSocket<float> _rotationZSocket;
    private readonly InputSocket<float> _centerXSocket;
    private readonly InputSocket<float> _centerYSocket;
    private readonly InputSocket<float> _centerZSocket;
    private readonly InputSocket<float> _depthSocket;
    private readonly Rotation3DTransform _model = new();

    public Rotation3DNode()
    {
        _rotationXSocket = AsInput(Rotation3DTransform.RotationXProperty).AcceptNumber();
        _rotationYSocket = AsInput(Rotation3DTransform.RotationYProperty).AcceptNumber();
        _rotationZSocket = AsInput(Rotation3DTransform.RotationZProperty).AcceptNumber();
        _centerXSocket = AsInput(Rotation3DTransform.CenterXProperty).AcceptNumber();
        _centerYSocket = AsInput(Rotation3DTransform.CenterYProperty).AcceptNumber();
        _centerZSocket = AsInput(Rotation3DTransform.CenterZProperty).AcceptNumber();
        _depthSocket = AsInput(Rotation3DTransform.DepthProperty).AcceptNumber();
    }

    protected override void EvaluateCore(EvaluationContext context)
    {
        _model.RotationX = _rotationXSocket.Value;
        _model.RotationY = _rotationYSocket.Value;
        _model.RotationZ = _rotationZSocket.Value;
        _model.CenterX = _centerXSocket.Value;
        _model.CenterY = _centerYSocket.Value;
        _model.CenterZ = _centerZSocket.Value;
        _model.Depth = _depthSocket.Value;
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
