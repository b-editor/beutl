namespace Beutl.NodeGraph.Nodes;

public partial class OutputNode : GraphNode
{
    public OutputNode()
    {
        InputPort = AddInput<object>("Output");
    }

    public InputPort<object> InputPort { get; }
}
