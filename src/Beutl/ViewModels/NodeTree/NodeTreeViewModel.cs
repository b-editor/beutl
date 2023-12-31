using System.Text.Json.Nodes;

using Avalonia;

using Beutl.NodeTree;
using Beutl.NodeTree.Nodes.Group;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeTreeViewModel : IDisposable, IJsonSerializable
{
    private readonly CompositeDisposable _disposables = [];

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

    public CoreList<NodeViewModel> Nodes { get; } = [];

    public ReactiveProperty<Matrix> Matrix { get; } = new(Avalonia.Matrix.Identity);

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

    public void AddSocket(Type type, Point point)
    {
        var node = (Node)Activator.CreateInstance(type)!;
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

    public void WriteToJson(JsonObject json)
    {
        var nodesJson = new JsonObject();
        foreach (NodeViewModel item in Nodes)
        {
            var nodeJson = new JsonObject();
            item.WriteToJson(nodeJson);
            nodesJson[item.Node.Id.ToString()] = nodeJson;
        }

        json[nameof(Nodes)] = nodesJson;

        Matrix m = Matrix.Value;
        json[nameof(Matrix)] = $"{m.M11},{m.M12},{m.M21},{m.M22},{m.M31},{m.M32}";
    }

    public void ReadFromJson(JsonObject json)
    {
        JsonObject nodesJson = json[nameof(Nodes)]!.AsObject();
        foreach (NodeViewModel item in Nodes)
        {
            if (nodesJson.TryGetPropertyValue(item.Node.Id.ToString(), out JsonNode? nodeJson))
            {
                item.ReadFromJson(nodeJson!.AsObject());
            }
        }

        if (json.TryGetPropertyValue(nameof(Matrix), out JsonNode? mJson))
        {
            string m = (string)mJson!;
            Matrix.Value = Avalonia.Matrix.Parse(m);
        }
    }
}
