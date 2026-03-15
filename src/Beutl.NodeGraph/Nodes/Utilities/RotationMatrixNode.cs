using Beutl.Graphics;
using Beutl.NodeGraph.Composition;
using Beutl.Utilities;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class RotationMatrixNode : MatrixNode
{
    public RotationMatrixNode()
    {
        Rotation = AddInput<float>("Rotation");
    }

    public InputPort<float> Rotation { get; }

    public partial class Resource
    {
        protected override Matrix GetMatrix(GraphCompositionContext context, MatrixNode node)
        {
            return Matrix.CreateRotation(MathUtilities.Deg2Rad(Rotation));
        }
    }
}
