using System.Text.Json.Nodes;
using Avalonia;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.Logging;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Generative;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public sealed class NodeGraphViewModel : IDisposable, IJsonSerializable
{
    private static readonly ILogger s_logger = Log.CreateLogger<NodeGraphViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private CancellationTokenSource? _generativeCts;

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

        CanRunGenerative = HasGenerativeNodes
            .CombineLatest(IsGenerating, (has, running) => has && !running)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        HasGenerativeNodes.Value = ContainsGenerativeNode();
        // Raised for nodes added or removed inside groups too, which the graph also runs.
        graph.TopologyChanged += OnTopologyChanged;

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

    /// <summary>True while a queue of generative nodes is running.</summary>
    public ReactivePropertySlim<bool> IsGenerating { get; } = new();

    /// <summary>True when the graph holds at least one AI node.</summary>
    public ReactivePropertySlim<bool> HasGenerativeNodes { get; } = new();

    /// <summary>Whether "Run AI nodes" can start now.</summary>
    public ReadOnlyReactivePropertySlim<bool> CanRunGenerative { get; }

    /// <summary>A reason the last queue could not start, for the toolbar.</summary>
    public ReactivePropertySlim<string?> GenerativeError { get; } = new();

    /// <summary>
    /// Runs <paramref name="targets"/> (every generative node when null) and the generative
    /// nodes they depend on. Nodes whose request is unchanged reuse their result unless forced.
    /// </summary>
    public async Task RunGenerativeAsync(IReadOnlyCollection<GenerativeNode>? targets, bool force, int variations = 1)
    {
        if (IsGenerating.Value)
            return;
        if (EditorContext.GetService<IGenerativeNodeExecutor>() is not { } executor)
        {
            GenerativeError.Value = NodeGraphStrings.Generative_ExecutorUnavailable;
            return;
        }

        GenerativeError.Value = null;
        IsGenerating.Value = true;
        using var cts = new CancellationTokenSource();
        _generativeCts = cts;
        try
        {
            var runner = new GenerativeGraphRunner(executor, new EditorGenerativeRunHost(EditorContext));
            await runner.RunAsync(NodeGraph, targets, force, cts.Token, variations);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to run generative nodes.");
            GenerativeError.Value = NodeGraphStrings.Generative_Failed;
        }
        finally
        {
            _generativeCts = null;
            IsGenerating.Value = false;
        }
    }

    public void CancelGenerative() => _generativeCts?.Cancel();

    private void OnTopologyChanged(object? sender, EventArgs e)
        => HasGenerativeNodes.Value = ContainsGenerativeNode();

    private bool ContainsGenerativeNode()
        => NodeGraph.EnumerateGraphs().SelectMany(graph => graph.Nodes).Any(node => node is GenerativeNode);

    public CoreList<GraphNodeViewModel> Nodes { get; } = [];

    public CoreList<ConnectionViewModel> AllConnections { get; } = [];

    public ReactiveProperty<Matrix> Matrix { get; } = new(Avalonia.Matrix.Identity);

    public GraphModel NodeGraph { get; }

    public NodePortViewModel? FindNodePortViewModel(INodePort port)
    {
        foreach (GraphNodeViewModel node in Nodes.GetMarshal().Value)
        {
            foreach (NodeMemberViewModel item in node.EnumerateMembers())
            {
                if (item.Model == port)
                {
                    return item as NodePortViewModel;
                }
            }
        }

        return null;
    }

    /// <summary>Where group templates are kept; replaced in tests.</summary>
    internal Beutl.NodeGraph.Nodes.Group.GroupNodeTemplates Templates { get; set; } =
        Beutl.NodeGraph.Nodes.Group.GroupNodeTemplates.Default;

    /// <summary>Adds a copy of a saved group at <paramref name="point"/>.</summary>
    public bool AddTemplate(Beutl.NodeGraph.Nodes.Group.GroupNodeTemplate template, Point point)
    {
        if (Templates.Instantiate(template) is not { } group)
        {
            GenerativeError.Value = NodeGraphStrings.Template_LoadFailed;
            return false;
        }

        return EditorContext.GetRequiredService<INodeGraphMutationService>()
            .AddNode(NodeGraph, group, point.X, point.Y);
    }

    public void AddNodePort(Type type, Point point)
    {
        var node = (GraphNode)Activator.CreateInstance(type)!;
        EditorContext.GetRequiredService<INodeGraphMutationService>()
            .AddNode(NodeGraph, node, point.X, point.Y);
    }

    public bool AddNodeAndConnect(GraphNode node, Point point, INodePort existingPort, INodePort? newPort)
        => EditorContext.GetRequiredService<INodeGraphMutationService>() is INodeGraphConnectedNodeMutationService service
           && service.AddNodeAndConnect(NodeGraph, node, point.X, point.Y, existingPort, newPort);

    internal bool TryAddSuggestedNode(CompatibleNodeFinder.Candidate candidate,
        CompatibleNodeFinder.PortChoice? choice, INodePort source, Point point)
    {
        if (!CompatibleNodeFinder.TryCreateSelection(candidate, choice, source,
                out GraphNode? node, out INodePort? port) || node == null)
            return false;

        bool added = false;
        try
        {
            added = AddNodeAndConnect(node, point, source, port);
            return added;
        }
        finally
        {
            if (!added && node.FindHierarchicalParent<GraphModel>() is null)
                CompatibleNodeFinder.DisposeRejectedNode(node, candidate.Registry.Type);
        }
    }

    public bool SupportsConnectedNodeCreation
        => EditorContext.GetService(typeof(INodeGraphMutationService)) is INodeGraphConnectedNodeMutationService;

    public void Dispose()
    {
        _generativeCts?.Cancel();
        NodeGraph.TopologyChanged -= OnTopologyChanged;
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
