using Beutl.Media;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public partial class PixelPointNode : Node
{
    public PixelPointNode()
    {
        Value = AddOutput<PixelPoint>("PixelPoint");
        X = AddInput<int>("X");
        Y = AddInput<int>("Y");
    }

    public OutputSocket<PixelPoint> Value { get; }

    public InputSocket<int> X { get; }

    public InputSocket<int> Y { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            Value = new PixelPoint(X, Y);
        }
    }
}
