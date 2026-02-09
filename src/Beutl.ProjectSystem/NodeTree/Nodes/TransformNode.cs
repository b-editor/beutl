using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree.Nodes;

public class TransformNode : ConfigureNode
{
    public TransformNode()
    {
        Matrix = AsInput<Matrix>("Matrix");
    }

    public InputSocket<Matrix> Matrix { get; }

    protected override void EvaluateCore(NodeEvaluationContext context)
    {
        var matrix = !Matrix.Connection.IsNull ? Matrix.Value : Graphics.Matrix.Identity;
        if (OutputSocket.Value == null)
        {
            OutputSocket.Value = new TransformRenderNode(matrix, TransformOperator.Prepend);
        }
        else if (OutputSocket.Value is TransformRenderNode node)
        {
            node.Update(matrix, TransformOperator.Prepend);
        }
    }
}
