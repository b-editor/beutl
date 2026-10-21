using System.ComponentModel;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.NodeGraph;

public class GraphGroup : GraphModel
{
    public GraphGroup()
    {
        ScanProperties<GraphGroup>();
        HideProperties(InputProperty, OutputProperty);
        InputProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Input)));
        OutputProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Output)));
        Nodes.Attached += OnNodeAttached;
        Nodes.Detached += OnNodeDetached;
    }

    // Input and Output name nodes that Nodes holds, so they do not take them as children.
    [NotAutoSerialized]
    [NotTracked]
    public IProperty<GroupInput?> InputProperty { get; } = new ValueOnlyProperty<GroupInput?>(null) { OwnsValue = false };

    [NotAutoSerialized]
    [NotTracked]
    public IProperty<GroupOutput?> OutputProperty { get; } = new ValueOnlyProperty<GroupOutput?>(null) { OwnsValue = false };

    public GroupInput? Input => InputProperty.Value;

    public GroupOutput? Output => OutputProperty.Value;

    private void OnNodeAttached(GraphNode obj)
    {
        if (obj is GroupInput groupInput)
        {
            TakePlace(InputProperty, groupInput);
        }
        if (obj is GroupOutput groupOutput)
        {
            TakePlace(OutputProperty, groupOutput);
        }
    }

    private void OnNodeDetached(GraphNode obj)
    {
        if (obj == Input)
        {
            InputProperty.Value = null;
        }
        if (obj == Output)
        {
            OutputProperty.Value = null;
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
