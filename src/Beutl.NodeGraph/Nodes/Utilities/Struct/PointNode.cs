using Beutl.Graphics;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities.Struct;

public partial class PointNode : GraphNode
{
    public PointNode()
    {
        Value = AddOutput<Point>("Point");
        X = AddInput<float>("X");
        Y = AddInput<float>("Y");
    }

    public OutputPort<Point> Value { get; }

    public InputPort<float> X { get; }

    public InputPort<float> Y { get; }

    public partial class Resource
    {
        protected override void UpdateCore(GraphCompositionContext context)
        {
            Value = new Point(X, Y);
        }
    }
}
