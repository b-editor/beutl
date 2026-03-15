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
        set => SetAndRaise(InputProperty, ref _input, value);
    }

    [NotAutoSerialized]
    [NotTracked]
    public GroupOutput? Output
    {
        get => _output;
        set => SetAndRaise(OutputProperty, ref _output, value);
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
        if (args is CorePropertyChangedEventArgs e)
        {
            if (e.Property == InputProperty
                || e.Property == OutputProperty)
            {
                int index = -1;

                if (e.OldValue is GraphNode oldNode)
                {
                    index = Nodes.IndexOf(oldNode);
                    Nodes.Remove(oldNode);
                }

                if (index == -1)
                {
                    index = Nodes.Count;
                }

                if (e.NewValue is GraphNode newNode)
                {
                    if (e.Property == InputProperty && Nodes.Any(x => x is GroupInput))
                        return;
                    else if (e.Property == OutputProperty && Nodes.Any(x => x is GroupOutput))
                        return;

                    Nodes.Insert(index, newNode);
                }
            }
        }
    }
}
