using Beutl.Media;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public partial class PixelSizeNode : Node
{
    public PixelSizeNode()
    {
        Value = AddOutput<PixelSize>("PixelSize");
        Width = AddInput<int>("Width");
        Height = AddInput<int>("Height");
    }

    public OutputSocket<PixelSize> Value { get; }

    public InputSocket<int> Width { get; }

    public InputSocket<int> Height { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            Value = new PixelSize(Width, Height);
        }
    }
}
