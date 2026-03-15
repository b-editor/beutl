using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public partial class TransformNode : ConfigureNode
{
    public TransformNode()
    {
        Matrix = AddInput<Matrix>("Matrix");
    }

    public InputPort<Matrix> Matrix { get; }

    public partial class Resource
    {
        protected override void UpdateCore(GraphCompositionContext context)
        {
            var node = GetOriginal();
            var matrix = context.HasConnection(node.Matrix)
                ? Matrix
                : Graphics.Matrix.Identity;

            var output = OutputPort;
            if (output == null)
            {
                OutputPort = new TransformRenderNode(matrix, TransformOperator.Prepend);
            }
            else if (output is TransformRenderNode trn)
            {
                trn.Update(matrix, TransformOperator.Prepend);
            }
        }
    }
}
