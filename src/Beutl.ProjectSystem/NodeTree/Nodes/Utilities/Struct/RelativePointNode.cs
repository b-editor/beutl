using Beutl.Graphics;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public partial class RelativePointNode : Node
{
    public RelativePointNode()
    {
        Value = AddOutput<RelativePoint>("RelativePoint");
        Unit = AddProperty<RelativeUnit>("Unit");
        X = AddInput<float>("X");
        Y = AddInput<float>("Y");
    }

    public OutputSocket<RelativePoint> Value { get; }

    public NodeItem<RelativeUnit> Unit { get; }

    public InputSocket<float> X { get; }

    public InputSocket<float> Y { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            Value = new RelativePoint(X, Y, Unit);
        }
    }
}
