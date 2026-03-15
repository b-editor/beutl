using Beutl.Graphics;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class ScaleMatrixNode : MatrixNode
{
    public ScaleMatrixNode()
    {
        Scale = AddInput<float>("Scale");
        ScaleX = AddInput<float>("ScaleX");
        ScaleY = AddInput<float>("ScaleY");
    }

    public InputPort<float> Scale { get; }

    public InputPort<float> ScaleX { get; }

    public InputPort<float> ScaleY { get; }

    public partial class Resource
    {
        protected override Matrix GetMatrix(GraphCompositionContext context, MatrixNode node)
        {
            return Matrix.CreateScale(Scale * ScaleX, Scale * ScaleY);
        }
    }
}
