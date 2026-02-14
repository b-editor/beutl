using Beutl.NodeTree.Rendering;

namespace Beutl.NodeTree.Nodes.Utilities;

public partial class SwitchNode : Node
{
    public SwitchNode()
    {
        Output = AddOutput<object?>("Output");
        Switch = AddInput<bool>("Switch");
        True = AddInput<object?>("True");
        False = AddInput<object?>("False");
    }

    public OutputSocket<object?> Output { get; }

    public InputSocket<bool> Switch { get; }

    public InputSocket<object?> True { get; }

    public InputSocket<object?> False { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            Output = Switch ? True : False;
        }
    }
}
