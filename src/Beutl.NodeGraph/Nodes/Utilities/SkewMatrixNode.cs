using Beutl.Graphics;
using Beutl.Utilities;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class SkewMatrixNode : MatrixNode
{
    public SkewMatrixNode()
    {
        SkewX = AddInput<float>("SkewX", NodePortDisplays.SkewX);
        SkewY = AddInput<float>("SkewY", NodePortDisplays.SkewY);
    }

    public InputPort<float> SkewX { get; }

    public InputPort<float> SkewY { get; }

    public partial class Resource
    {
        protected override Matrix GetMatrix()
        {
            return Matrix.CreateSkew(
                MathUtilities.Deg2Rad(SkewX),
                MathUtilities.Deg2Rad(SkewY));
        }
    }
}
