using Beutl.Graphics;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class TranslateMatrixNode : MatrixNode
{
    public TranslateMatrixNode()
    {
        X = AddInput<float>("X", NodePortDisplays.X);
        Y = AddInput<float>("Y", NodePortDisplays.Y);
    }

    public InputPort<float> X { get; }

    public InputPort<float> Y { get; }

    public partial class Resource
    {
        protected override Matrix GetMatrix()
        {
            return Matrix.CreateTranslation(X, Y);
        }
    }
}
