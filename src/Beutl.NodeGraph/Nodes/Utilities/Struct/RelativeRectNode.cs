using Beutl.Graphics;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities.Struct;

public partial class RelativeRectNode : GraphNode
{
    public RelativeRectNode()
    {
        Value = AddOutput<RelativeRect>("RelativeRect", NodePortDisplays.RelativeRect);
        Unit = AddProperty<RelativeUnit>("Unit", NodePortDisplays.Unit);
        X = AddInput<float>("X", NodePortDisplays.X);
        Y = AddInput<float>("Y", NodePortDisplays.Y);
        Width = AddInput<float>("Width", NodePortDisplays.Width);
        Height = AddInput<float>("Height", NodePortDisplays.Height);
        Width.Property?.SetValue(1);
        Height.Property?.SetValue(1);
    }

    public OutputPort<RelativeRect> Value { get; }

    public NodeMember<RelativeUnit> Unit { get; }

    public InputPort<float> X { get; }

    public InputPort<float> Y { get; }

    public InputPort<float> Width { get; }

    public InputPort<float> Height { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Value = new RelativeRect(new Point(X, Y), new Size(Width, Height), Unit);
        }
    }
}
