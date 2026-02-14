using Beutl.Graphics;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public partial class PointNode : Node
{
    public PointNode()
    {
        Value = AddOutput<Point>("Point");
        X = AddInput<float>("X");
        Y = AddInput<float>("Y");
    }

    public OutputSocket<Point> Value { get; }

    public InputSocket<float> X { get; }

    public InputSocket<float> Y { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            Value = new Point(X, Y);
        }
    }
}
