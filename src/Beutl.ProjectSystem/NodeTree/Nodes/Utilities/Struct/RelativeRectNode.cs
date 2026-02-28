using Beutl.Graphics;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public partial class RelativeRectNode : Node
{
    public RelativeRectNode()
    {
        Value = AddOutput<RelativeRect>("RelativeRect");
        Unit = AddProperty<RelativeUnit>("Unit");
        X = AddInput<float>("X");
        Y = AddInput<float>("Y");
        Width = AddInput<float>("Width");
        Height = AddInput<float>("Height");
        Width.Property?.SetValue(1);
        Height.Property?.SetValue(1);
    }

    public OutputSocket<RelativeRect> Value { get; }

    public NodeItem<RelativeUnit> Unit { get; }

    public InputSocket<float> X { get; }

    public InputSocket<float> Y { get; }

    public InputSocket<float> Width { get; }

    public InputSocket<float> Height { get; }

    public partial class Resource
    {
        public override void Update(NodeCompositionContext context)
        {
            Value = new RelativeRect(new Point(X, Y), new Size(Width, Height), Unit);
        }
    }
}
