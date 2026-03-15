using Beutl.Graphics;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities.Struct;

public partial class SizeNode : GraphNode
{
    public SizeNode()
    {
        Value = AddOutput<Size>("Size");
        Width = AddInput<float>("Width");
        Height = AddInput<float>("Height");
    }

    public OutputPort<Size> Value { get; }

    public InputPort<float> Width { get; }

    public InputPort<float> Height { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Value = new Size(Width, Height);
        }
    }
}
