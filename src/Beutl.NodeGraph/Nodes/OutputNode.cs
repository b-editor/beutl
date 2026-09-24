namespace Beutl.NodeGraph.Nodes;

public partial class OutputNode : GraphNode
{
    public OutputNode()
    {
        InputPort = AddInput<object>("Output", NodePortDisplays.Output);
    }

    public InputPort<object> InputPort { get; }
}
