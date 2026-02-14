using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities;

public partial class TranslateMatrixNode : MatrixNode
{
    public TranslateMatrixNode()
    {
        X = AddInput<float>("X");
        Y = AddInput<float>("Y");
    }

    public InputSocket<float> X { get; }

    public InputSocket<float> Y { get; }

    public partial class Resource
    {
        protected override Matrix GetMatrix(NodeRenderContext context, MatrixNode node)
        {
            return Matrix.CreateTranslation(X, Y);
        }
    }
}
