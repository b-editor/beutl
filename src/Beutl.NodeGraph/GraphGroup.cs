using System.ComponentModel;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.NodeGraph;

public class GraphGroup : GraphModel
{
    public static readonly CoreProperty<GroupInput?> InputProperty;
    public static readonly CoreProperty<GroupOutput?> OutputProperty;

    private GroupInput? _input;
    private GroupOutput? _output;

    static GraphGroup()
    {
        InputProperty = ConfigureProperty<GroupInput?, GraphGroup>(o => o.Input)
            .Register();

        OutputProperty = ConfigureProperty<GroupOutput?, GraphGroup>(o => o.Output)
            .Register();
    }

    public GraphGroup()
    {
        Nodes.Attached += OnNodeAttached;
        Nodes.Detached += OnNodeDetached;
    }

    [NotAutoSerialized]
    [NotTracked]
    public GroupInput? Input
    {
        get => _input;
        private set => SetAndRaise(InputProperty, ref _input, value);
    }

    [NotAutoSerialized]
    [NotTracked]
    public GroupOutput? Output
    {
        get => _output;
        private set => SetAndRaise(OutputProperty, ref _output, value);
    }

    private void OnNodeAttached(GraphNode obj)
    {
        if (obj is GroupInput groupInput)
        {
            Input = groupInput;
        }
        if (obj is GroupOutput groupOutput)
        {
            Output = groupOutput;
        }
    }

    private void OnNodeDetached(GraphNode obj)
    {
        if (obj == Input)
        {
            Input = null;
        }
        if (obj == Output)
        {
            Output = null;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        // Input and Output are set as a node is attached, when it is already in Nodes, so a
        // second GroupInput or GroupOutput only has to take the place of the first.
        if (args is CorePropertyChangedEventArgs e
            && (e.Property == InputProperty || e.Property == OutputProperty)
            && e.OldValue is GraphNode oldNode)
        {
            Nodes.Remove(oldNode);
        }
    }
}
