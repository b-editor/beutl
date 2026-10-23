using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.NodeGraph;

public class GraphGroup : GraphModel
{
    public GraphGroup()
    {
        ScanProperties<GraphGroup>();
        HideProperties(Input, Output);
        Nodes.Attached += OnNodeAttached;
        Nodes.Detached += OnNodeDetached;
    }

    // Input and Output name nodes that Nodes holds, so they do not take them as children.
    [NotAutoSerialized]
    [NotTracked]
    public IProperty<GroupInput?> Input { get; } = new ValueOnlyProperty<GroupInput?>(null) { OwnsValue = false, AffectsRender = false };

    [NotAutoSerialized]
    [NotTracked]
    public IProperty<GroupOutput?> Output { get; } = new ValueOnlyProperty<GroupOutput?>(null) { OwnsValue = false, AffectsRender = false };

    private void OnNodeAttached(GraphNode obj)
    {
        if (obj is GroupInput groupInput)
        {
            TakePlace(Input, groupInput);
        }
        if (obj is GroupOutput groupOutput)
        {
            TakePlace(Output, groupOutput);
        }
    }

    private void OnNodeDetached(GraphNode obj)
    {
        if (obj == Input.Value)
        {
            Input.Value = null;
        }
        if (obj == Output.Value)
        {
            Output.Value = null;
        }
    }

    // A second GroupInput or GroupOutput takes the place of the first, which leaves Nodes.
    private void TakePlace<TNode>(IProperty<TNode?> property, TNode node)
        where TNode : GraphNode
    {
        TNode? previous = property.Value;
        property.Value = node;
        if (previous != null && previous != node)
        {
            Nodes.Remove(previous);
        }
    }
}
