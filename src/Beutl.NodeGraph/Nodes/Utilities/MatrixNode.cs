using Beutl.Graphics;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities;

public abstract partial class MatrixNode : GraphNode
{
    public MatrixNode()
    {
        Output = AddOutput<Matrix>("Output");
        Input = AddInput<Matrix>("Input");
    }

    public OutputPort<Matrix> Output { get; }

    public InputPort<Matrix> Input { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = GetOriginal();
            Matrix matrix = GetMatrix(context, node);

            if (context.HasConnection(node.Input))
            {
                Output = matrix * Input;
            }
            else
            {
                Output = matrix;
            }
        }

        protected virtual Matrix GetMatrix(GraphCompositionContext context, MatrixNode node)
            => Matrix.Identity;
    }
}
