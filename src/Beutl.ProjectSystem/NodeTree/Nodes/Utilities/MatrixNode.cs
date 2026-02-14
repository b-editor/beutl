using Beutl.Graphics;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities;

public abstract partial class MatrixNode : Node
{
    public MatrixNode()
    {
        Output = AddOutput<Matrix>("Output");
        Input = AddInput<Matrix>("Input");
    }

    public OutputSocket<Matrix> Output { get; }

    public InputSocket<Matrix> Input { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
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

        protected virtual Matrix GetMatrix(NodeRenderContext context, MatrixNode node)
            => Matrix.Identity;
    }
}
