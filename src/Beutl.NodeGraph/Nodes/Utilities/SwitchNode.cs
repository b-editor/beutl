using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class SwitchNode : GraphNode
{
    public SwitchNode()
    {
        Output = AddOutput<object?>("Output");
        Switch = AddInput<bool>("Switch");
        True = AddInput<object?>("True");
        False = AddInput<object?>("False");
    }

    public OutputPort<object?> Output { get; }

    public InputPort<bool> Switch { get; }

    public InputPort<object?> True { get; }

    public InputPort<object?> False { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Output = Switch ? True : False;
        }
    }
}
