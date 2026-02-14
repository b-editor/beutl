using Beutl.Graphics;
using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities.Struct;

public partial class SizeNode : Node
{
    public SizeNode()
    {
        Value = AddOutput<Size>("Size");
        Width = AddInput<float>("Width");
        Height = AddInput<float>("Height");
    }

    public OutputSocket<Size> Value { get; }

    public InputSocket<float> Width { get; }

    public InputSocket<float> Height { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            Value = new Size(Width, Height);
        }
    }
}
