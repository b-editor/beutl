using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.NodeTree.Rendering;
using Beutl.Utilities;

namespace Beutl.NodeTree.Nodes.Utilities;

public partial class RotationMatrixNode : MatrixNode
{
    public RotationMatrixNode()
    {
        Rotation = AddInput<float>("Rotation");
    }

    public InputSocket<float> Rotation { get; }

    public partial class Resource
    {
        protected override Matrix GetMatrix(NodeRenderContext context, MatrixNode node)
        {
            return Matrix.CreateRotation(MathUtilities.Deg2Rad(Rotation));
        }
    }
}
