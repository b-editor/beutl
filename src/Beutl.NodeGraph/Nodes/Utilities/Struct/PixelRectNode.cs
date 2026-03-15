using Beutl.Media;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities.Struct;

public partial class PixelRectNode : GraphNode
{
    public PixelRectNode()
    {
        Value = AddOutput<PixelRect>("PixelRect");
        Position = AddInput<PixelPoint>("Position");
        Size = AddInput<PixelSize>("Size");
    }

    public OutputPort<PixelRect> Value { get; }

    public new InputPort<PixelPoint> Position { get; }

    public InputPort<PixelSize> Size { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Value = new PixelRect(Position, Size);
        }
    }
}
