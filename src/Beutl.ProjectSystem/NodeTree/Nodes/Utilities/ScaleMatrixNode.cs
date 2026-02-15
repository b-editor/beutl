using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities;

public partial class ScaleMatrixNode : MatrixNode
{
    public ScaleMatrixNode()
    {
        Scale = AddInput<float>("Scale");
        ScaleX = AddInput<float>("ScaleX");
        ScaleY = AddInput<float>("ScaleY");
    }

    public InputSocket<float> Scale { get; }

    public InputSocket<float> ScaleX { get; }

    public InputSocket<float> ScaleY { get; }

    public partial class Resource
    {
        protected override Matrix GetMatrix(NodeRenderContext context, MatrixNode node)
        {
            return Matrix.CreateScale(Scale * ScaleX, Scale * ScaleY);
        }
    }
}
