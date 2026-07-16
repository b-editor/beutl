using Beutl.Media;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities.Struct;

public partial class PixelPointNode : GraphNode
{
    public PixelPointNode()
    {
        Value = AddOutput<PixelPoint>("PixelPoint");
        X = AddInput<int>("X");
        Y = AddInput<int>("Y");
    }

    public OutputPort<PixelPoint> Value { get; }

    public InputPort<int> X { get; }

    public InputPort<int> Y { get; }

    public partial class Resource
    {
        protected override void UpdateCore(GraphCompositionContext context)
        {
            Value = new PixelPoint(X, Y);
        }
    }
}
