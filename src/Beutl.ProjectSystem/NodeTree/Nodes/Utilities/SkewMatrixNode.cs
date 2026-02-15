using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.NodeTree.Rendering;
using Beutl.Utilities;

namespace Beutl.NodeTree.Nodes.Utilities;

public partial class SkewMatrixNode : MatrixNode
{
    public SkewMatrixNode()
    {
        SkewX = AddInput<float>("SkewX");
        SkewY = AddInput<float>("SkewY");
    }

    public InputSocket<float> SkewX { get; }

    public InputSocket<float> SkewY { get; }

    public partial class Resource
    {
        protected override Matrix GetMatrix(NodeRenderContext context, MatrixNode node)
        {
            return Matrix.CreateSkew(
                MathUtilities.Deg2Rad(SkewX),
                MathUtilities.Deg2Rad(SkewY));
        }
    }
}
