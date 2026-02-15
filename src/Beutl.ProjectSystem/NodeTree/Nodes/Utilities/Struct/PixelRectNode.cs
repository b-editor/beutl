using Beutl.Media;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public partial class PixelRectNode : Node
{
    public PixelRectNode()
    {
        Value = AddOutput<PixelRect>("PixelRect");
        Position = AddInput<PixelPoint>("Position");
        Size = AddInput<PixelSize>("Size");
    }

    public OutputSocket<PixelRect> Value { get; }

    public new InputSocket<PixelPoint> Position { get; }

    public InputSocket<PixelSize> Size { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            Value = new PixelRect(Position, Size);
        }
    }
}
