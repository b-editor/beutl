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

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new ConfigureNodeEvaluationState(null, new Rotation3DTransform());
    }

    protected override void EvaluateCore(NodeEvaluationContext context)
    {
        if (context.State is ConfigureNodeEvaluationState { AddtionalState: Rotation3DTransform model })
        {
            model.RotationX = _rotationXSocket.Value;
            model.RotationY = _rotationYSocket.Value;
            model.RotationZ = _rotationZSocket.Value;
            model.CenterX = _centerXSocket.Value;
            model.CenterY = _centerYSocket.Value;
            model.CenterZ = _centerZSocket.Value;
            model.Depth = _depthSocket.Value;
        }
    }

    protected override void Attach(Drawable drawable, object? state)
    {
        if (state is Rotation3DTransform model)
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
        if (state is Rotation3DTransform model
            && drawable.Transform is TransformGroup group)
        {
            group.Children.Remove(model);
        }
    }
}
