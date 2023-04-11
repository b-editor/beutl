using Avalonia;

using Beutl.NodeTree;
using Beutl.NodeTree.Nodes.Group;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeTreeViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public NodeTreeViewModel(NodeTreeModel nodeTree)
    {
        NodeTree = nodeTree;

        nodeTree.Nodes.ForEachItem(
            (idx, item) =>
            {
                var viewModel = new NodeViewModel(item);
                Nodes.Insert(idx, viewModel);
            },
            (idx, _) =>
            {
                NodeViewModel viewModel = Nodes[idx];
                Nodes.RemoveAt(idx);
                viewModel.Dispose();
            },
            () =>
            {
                foreach (NodeViewModel item in Nodes.GetMarshal().Value)
                {
                    item.Dispose();
                }
                Nodes.Clear();
            })
            .DisposeWith(_disposables);
    }

    public CoreList<NodeViewModel> Nodes { get; } = new();

    public NodeTreeModel NodeTree { get; }

    public SocketViewModel? FindSocketViewModel(ISocket socket)
    {
        foreach (NodeViewModel node in Nodes.GetMarshal().Value)
        {
            foreach (NodeItemViewModel item in node.Items.GetMarshal().Value)
            {
                if (item.Model == socket)
                {
                    return item as SocketViewModel;
                }
            }
        }

        return null;
    }

    public void AddSocket(NodeRegistry.RegistryItem item, Point point)
    {
        var node = (Node)Activator.CreateInstance(item.Type)!;
        node.Position = (point.X, point.Y);
        if (NodeTree is NodeGroup nodeGroup)
        {
            if (node is GroupInput
                && nodeGroup.Nodes.Any(x => x is GroupInput))
            {
                return;
            }
            else if (node is GroupOutput
                && nodeGroup.Nodes.Any(x => x is GroupOutput))
            {
                return;
            }
        }

        NodeTree.Nodes.BeginRecord<Node>()
            .Add(node)
            .ToCommand()
            .DoAndRecord(CommandRecorder.Default);
    }

    public void Dispose()
    {
        foreach (NodeViewModel item in Nodes)
        {
            item.Dispose();
        }
        Nodes.Clear();

        _disposables.Dispose();
    }
}
