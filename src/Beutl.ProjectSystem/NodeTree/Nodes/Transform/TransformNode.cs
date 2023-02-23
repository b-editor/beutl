using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class TransformNode : ConfigureNode
{
    private readonly InputSocket<Matrix> _matrixSocket;

    public TransformNode()
    {
        _matrixSocket = AsInput<Matrix>("Matrix");
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new ConfigureNodeEvaluationState(null, new MatrixTransform());
    }

    protected override void EvaluateCore(Drawable drawable, object? state)
    {
        if (state is MatrixTransform model
            && drawable.Transform is SpecializedTransformGroup group)
        {
            if (_matrixSocket.Connection != null)
            {
                model.Matrix = _matrixSocket.Value;
            }
            else
            {
                model.Matrix = Matrix.Identity;
            }

            group.AcceptTransform(model);
        }
    }

    protected override void Attach(Drawable drawable, object? state)
    {
        if (state is MatrixTransform model
            && drawable.Transform is SpecializedTransformGroup group)
        {
            group.Children.Add(model);
        }
    }

    protected override void Detach(Drawable drawable, object? state)
    {
        if (state is MatrixTransform model
            && drawable.Transform is SpecializedTransformGroup group)
        {
            group.Children.Remove(model);
        }
    }
}
