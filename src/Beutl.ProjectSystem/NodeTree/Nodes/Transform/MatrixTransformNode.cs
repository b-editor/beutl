﻿using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public sealed class MatrixTransformNode : TransformNode
{
    private readonly InputSocket<Matrix> _matrixSocket;

    public MatrixTransformNode()
    {
        _matrixSocket = AsInput<Matrix>("Matrix");
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new TransformNodeEvaluationState(new MatrixTransform());
    }

    protected override void EvaluateCore(ITransform? state)
    {
        if (state is MatrixTransform model)
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
}
