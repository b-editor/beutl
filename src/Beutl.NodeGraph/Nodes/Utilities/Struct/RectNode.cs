using Beutl.Graphics;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities.Struct;

public partial class RectNode : GraphNode
{
    public RectNode()
    {
        Value = AddOutput<Rect>("Rect", NodePortDisplays.Rect);
        Position = AddInput<Point>("TopLeft", NodePortDisplays.TopLeft);
        Size = AddInput<Size>("Size", NodePortDisplays.Size);
    }

    public OutputPort<Rect> Value { get; }

    public new InputPort<Point> Position { get; }

    public InputPort<Size> Size { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Value = new Rect(Position, Size);
        }
    }
}
