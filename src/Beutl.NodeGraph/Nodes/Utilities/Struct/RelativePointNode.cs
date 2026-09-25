using Beutl.Graphics;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities.Struct;

public partial class RelativePointNode : GraphNode
{
    public RelativePointNode()
    {
        Value = AddOutput<RelativePoint>("RelativePoint", NodePortDisplays.RelativePoint);
        Unit = AddProperty<RelativeUnit>("Unit", NodePortDisplays.Unit);
        X = AddInput<float>("X", NodePortDisplays.X);
        Y = AddInput<float>("Y", NodePortDisplays.Y);
    }

    public OutputPort<RelativePoint> Value { get; }

    public NodeMember<RelativeUnit> Unit { get; }

    public InputPort<float> X { get; }

    public InputPort<float> Y { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Value = new RelativePoint(X, Y, Unit);
        }
    }
}
