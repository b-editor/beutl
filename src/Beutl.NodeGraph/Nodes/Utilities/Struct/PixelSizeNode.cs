using Beutl.Media;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities.Struct;

public partial class PixelSizeNode : GraphNode
{
    public PixelSizeNode()
    {
        Value = AddOutput<PixelSize>("PixelSize");
        Width = AddInput<int>("Width");
        Height = AddInput<int>("Height");
    }

    public OutputPort<PixelSize> Value { get; }

    public InputPort<int> Width { get; }

    public InputPort<int> Height { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Value = new PixelSize(Width, Height);
        }
    }
}
