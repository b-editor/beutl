using Beutl.NodeTree;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeTreeViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public NodeTreeViewModel(NodeTreeSpace nodeTree)
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

    public NodeTreeSpace NodeTree { get; }

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
