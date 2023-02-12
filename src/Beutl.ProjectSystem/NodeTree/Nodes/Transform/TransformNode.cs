using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class TransformNode : ConfigureNode
{
    private readonly InputSocket<Matrix> _matrixSocket;

    public TransformNode()
    {
        _matrixSocket = AsInput<Matrix>("Matrix", "Matrix");
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new ConfigureNodeEvaluationState(null, new MatrixTransform());
    }

    protected override void EvaluateCore(NodeEvaluationContext context)
    {
        if (context.State is ConfigureNodeEvaluationState { AddtionalState: MatrixTransform model })
        {
            if (_matrixSocket.Connection != null)
            {
                model.Matrix = _matrixSocket.Value;
            }
            else
            {
                model.Matrix = Matrix.Identity;
            }
        }
    }

    protected override void Attach(Drawable drawable, object? state)
    {
        if (state is MatrixTransform model)
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
        if (state is MatrixTransform model
            && drawable.Transform is TransformGroup group)
        {
            group.Children.Remove(model);
        }
    }
}
