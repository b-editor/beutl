using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class Rotation3DMatrixNode : MatrixNode
{
    public Rotation3DMatrixNode()
    {
        RotationX = AddInput<float>("RotationX", NodePortDisplays.RotationX);
        RotationY = AddInput<float>("RotationY", NodePortDisplays.RotationY);
        RotationZ = AddInput<float>("RotationZ", NodePortDisplays.RotationZ);
        CenterX = AddInput<float>("CenterX", NodePortDisplays.CenterX);
        CenterY = AddInput<float>("CenterY", NodePortDisplays.CenterY);
        CenterZ = AddInput<float>("CenterZ", NodePortDisplays.CenterZ);
        Depth = AddInput<float>("Depth", NodePortDisplays.Depth);
    }

    public InputPort<float> RotationX { get; }

    public InputPort<float> RotationY { get; }

    public InputPort<float> RotationZ { get; }

    public InputPort<float> CenterX { get; }

    public InputPort<float> CenterY { get; }

    public InputPort<float> CenterZ { get; }

    public InputPort<float> Depth { get; }

    public partial class Resource
    {
        protected override Matrix GetMatrix(GraphCompositionContext context, MatrixNode node)
        {
            return Rotation3DTransform.CreateMatrix(
                RotationX, RotationY, RotationZ,
                CenterX, CenterY, CenterZ,
                Depth);
        }
    }
}
