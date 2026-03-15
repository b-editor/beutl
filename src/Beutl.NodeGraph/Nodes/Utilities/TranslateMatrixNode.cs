using Beutl.Graphics;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class TranslateMatrixNode : MatrixNode
{
    public TranslateMatrixNode()
    {
        X = AddInput<float>("X");
        Y = AddInput<float>("Y");
    }

    public InputPort<float> X { get; }

    public InputPort<float> Y { get; }

    public partial class Resource
    {
        protected override Matrix GetMatrix(GraphCompositionContext context, MatrixNode node)
        {
            return Matrix.CreateTranslation(X, Y);
        }
    }
}
