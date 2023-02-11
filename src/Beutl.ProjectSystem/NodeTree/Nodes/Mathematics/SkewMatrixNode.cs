using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Utilities;

namespace Beutl.NodeTree.Nodes.Mathematics;

public class SkewMatrixNode : MatrixNode
{
    private readonly InputSocket<float> _skewXSocket;
    private readonly InputSocket<float> _skewYSocket;

    public SkewMatrixNode()
    {
        _skewXSocket = AsInput(SkewTransform.SkewXProperty);
        _skewYSocket = AsInput(SkewTransform.SkewYProperty);
    }

    public override Matrix GetMatrix(EvaluationContext context) => Matrix.CreateSkew(MathUtilities.ToRadians(_skewXSocket.Value), MathUtilities.ToRadians(_skewYSocket.Value));
}
