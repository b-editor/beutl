using Beutl.Graphics;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public partial class RectNode : Node
{
    public RectNode()
    {
        Value = AddOutput<Rect>("Rect");
        Position = AddInput<Point>("TopLeft");
        Size = AddInput<Size>("Size");
    }

    public OutputSocket<Rect> Value { get; }

    public new InputSocket<Point> Position { get; }

    public InputSocket<Size> Size { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            Value = new Rect(Position, Size);
        }
    }
}
