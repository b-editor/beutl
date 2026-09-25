using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes.Utilities;

public partial class SwitchNode : GraphNode
{
    public SwitchNode()
    {
        Output = AddOutput<object?>("Output", NodePortDisplays.Output);
        Switch = AddInput<bool>("Switch", NodePortDisplays.Switch);
        True = AddInput<object?>("True", NodePortDisplays.True);
        False = AddInput<object?>("False", NodePortDisplays.False);
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
