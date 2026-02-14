using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes;

public partial class TransformNode : ConfigureNode
{
    public TransformNode()
    {
        Matrix = AddInput<Matrix>("Matrix");
    }

    public InputSocket<Matrix> Matrix { get; }

    public partial class Resource
    {
        protected override void EvaluateCore(NodeRenderContext context)
        {
            var node = GetOriginal();
            var matrix = context.HasConnection(node.Matrix)
                ? Matrix
                : Graphics.Matrix.Identity;

            var output = OutputSocket;
            if (output == null)
            {
                OutputSocket = new TransformRenderNode(matrix, TransformOperator.Prepend);
            }
            else if (output is TransformRenderNode trn)
            {
                trn.Update(matrix, TransformOperator.Prepend);
            }
        }
    }
}
