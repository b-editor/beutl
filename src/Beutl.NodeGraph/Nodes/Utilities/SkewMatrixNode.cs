using Beutl.Graphics;
using Beutl.NodeGraph.Composition;
using Beutl.Utilities;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class SkewMatrixNode : MatrixNode
{
    public SkewMatrixNode()
    {
        SkewX = AddInput<float>("SkewX");
        SkewY = AddInput<float>("SkewY");
    }

    public InputPort<float> SkewX { get; }

    public InputPort<float> SkewY { get; }

    public partial class Resource
    {
        protected override Matrix GetMatrix(GraphCompositionContext context, MatrixNode node)
        {
            return Matrix.CreateSkew(
                MathUtilities.Deg2Rad(SkewX),
                MathUtilities.Deg2Rad(SkewY));
        }
    }
}
