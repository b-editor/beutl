using Beutl.Graphics.Rendering;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes;

public partial class OutputNode : Node
{
    public OutputNode()
    {
        InputSocket = AddInput<RenderNode>("Output");
    }

    protected InputSocket<RenderNode> InputSocket { get; }

    public partial class Resource
    {
        private readonly RenderNodeDrawable _drawable = new();

        public override void Update(NodeRenderContext context)
        {
            RenderNode? input = InputSocket;
            _drawable.Node = input;
            context.AddRenderable(_drawable);
        }
    }
}
