using System.Text.Json.Nodes;
using Avalonia;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public sealed class NodeGraphViewModel : IDisposable, IJsonSerializable
{
    private readonly CompositeDisposable _disposables = [];

    public NodeGraphViewModel(GraphModel graph, IEditorContext editorContext)
    {
        NodeGraph = graph;
        EditorContext = editorContext;
        graph.Nodes.ForEachItem(
                (idx, item) =>
                {
                    var viewModel = new GraphNodeViewModel(item, this);
                    Nodes.Insert(idx, viewModel);
                },
                (idx, _) =>
                {
                    GraphNodeViewModel viewModel = Nodes[idx];
                    Nodes.RemoveAt(idx);
                    viewModel.Dispose();
                },
                () =>
                {
                    foreach (GraphNodeViewModel item in Nodes.GetMarshal().Value)
                    {
                        item.Dispose();
                    }

                    Nodes.Clear();
                })
            .DisposeWith(_disposables);

        graph.AllConnections.ForEachItem(
                item =>
                {
                    // NodePortViewModel側が既に追加している場合はスキップする
                    if (AllConnections.Any(i => i.Connection.Id == item.Id)) return;

                    var viewModel = new ConnectionViewModel(this, item);
                    AllConnections.Add(viewModel);
                },
                item =>
                {
                    ConnectionViewModel? viewModel = AllConnections.FirstOrDefault(i => i.Connection == item);
                    if (viewModel != null)
                    {
                        AllConnections.Remove(viewModel);
                        viewModel.Dispose();
                    }
                },
                () =>
                {
                    foreach (ConnectionViewModel conn in AllConnections)
                    {
                        conn.Dispose();
                    }

                    AllConnections.Clear();
                })
            .DisposeWith(_disposables);
    }

    public IEditorContext EditorContext { get; }

    public CoreList<GraphNodeViewModel> Nodes { get; } = [];

    public CoreList<ConnectionViewModel> AllConnections { get; } = [];

    public ReactiveProperty<Matrix> Matrix { get; } = new(Avalonia.Matrix.Identity);

    public GraphModel NodeGraph { get; }

    public NodePortViewModel? FindNodePortViewModel(INodePort port)
    {
        foreach (GraphNodeViewModel node in Nodes.GetMarshal().Value)
        {
            foreach (NodeMemberViewModel item in node.Items.GetMarshal().Value)
            {
                if (item.Model == port)
                {
                    return item as NodePortViewModel;
                }
            }
        }

        return null;
    }

    public void AddNodePort(Type type, Point point)
    {
        var node = (GraphNode)Activator.CreateInstance(type)!;
        node.Position = (point.X, point.Y);
        if (NodeGraph is GraphGroup nodeGroup)
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

        NodeGraph.Nodes.Add(node);
        EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.AddNode);
    }

    public void Dispose()
    {
        foreach (ConnectionViewModel conn in AllConnections)
        {
            conn.Dispose();
        }

        AllConnections.Clear();

        foreach (GraphNodeViewModel item in Nodes)
        {
            item.Dispose();
        }

        Nodes.Clear();

        _disposables.Dispose();
    }

    public void WriteToJson(JsonObject json)
    {
        var nodesJson = new JsonObject();
        foreach (GraphNodeViewModel item in Nodes)
        {
            var nodeJson = new JsonObject();
            item.WriteToJson(nodeJson);
            nodesJson[item.GraphNode.Id.ToString()] = nodeJson;
        }

        json[nameof(Nodes)] = nodesJson;

        Matrix m = Matrix.Value;
        json[nameof(Matrix)] = $"{m.M11},{m.M12},{m.M21},{m.M22},{m.M31},{m.M32}";
    }

    public void ReadFromJson(JsonObject json)
    {
        JsonObject nodesJson = json[nameof(Nodes)]!.AsObject();
        foreach (GraphNodeViewModel item in Nodes)
        {
            if (nodesJson.TryGetPropertyValue(item.GraphNode.Id.ToString(), out JsonNode? nodeJson))
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
