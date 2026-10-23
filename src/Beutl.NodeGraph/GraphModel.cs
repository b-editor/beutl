using Beutl.Collections;
using Beutl.Engine;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

[SuppressResourceClassGeneration]
public partial class GraphModel : EngineObject
{
    private Dictionary<IProperty, (IInputPort Port, int Count)>? _connectedInputProperties;

    public event EventHandler? TopologyChanged;

    public GraphModel()
    {
        ScanProperties<GraphModel>();
        HideProperties(Nodes, AllConnections);
        Nodes.Attached += OnNodeAttached;
        Nodes.Detached += OnNodeDetached;
        AllConnections.Attached += OnConnectionAttached;
        AllConnections.Detached += OnConnectionDetached;
    }

    private void OnConnectionDetached(Connection obj)
    {
        RaiseTopologyChanged();
        RaiseEdited();
    }

    private void OnConnectionAttached(Connection obj)
    {
        RaiseTopologyChanged();
        RaiseEdited();
    }

    private void OnTopologyChanged(object? sender, EventArgs e)
    {
        RaiseTopologyChanged();
    }

    private void OnNodeEdited(object? sender, EventArgs e)
    {
        RaiseEdited();
    }

    private void OnNodeDetached(GraphNode obj)
    {
        obj.TopologyChanged -= OnTopologyChanged;
        obj.Edited -= OnNodeEdited;
        RaiseTopologyChanged();
        if (obj is GroupNode group) group.Group.Value.RaiseTopologyChanged();
        RaiseEdited();
    }

    private void OnNodeAttached(GraphNode obj)
    {
        obj.TopologyChanged += OnTopologyChanged;
        obj.Edited += OnNodeEdited;
        RaiseTopologyChanged();
        RaiseEdited();
    }

    public IListProperty<GraphNode> Nodes { get; } = new ListProperty<GraphNode> { AffectsRender = false };

    public IListProperty<Connection> AllConnections { get; } = new ListProperty<Connection> { AffectsRender = false };

    public override void Deserialize(ICoreSerializationContext context)
    {
        // The lists keep their instances, as the CoreProperty accessors did, and are read first, where those were.
        KeptListReader.ReadInto<CoreList<GraphNode>, GraphNode>(context, Nodes.Name, Nodes);
        KeptListReader.ReadInto<CoreList<Connection>, Connection>(context, AllConnections.Name, AllConnections);
        base.Deserialize(context);
    }

    public Connection Connect(IInputPort inputNodePort, IOutputPort outputNodePort)
    {
        if (inputNodePort.FindHierarchicalParent<GraphNode>() is { } owner
            && !owner.CanConnectInput(inputNodePort))
            throw new InvalidOperationException("This input cannot be connected in the current graph state.");
        var connection = new Connection(inputNodePort, outputNodePort);
        connection.Connect();
        AllConnections.Add(connection);
        return connection;
    }

    public void Disconnect(Connection connection)
    {
        AllConnections.Remove(connection);
        connection.Disconnect();
    }

    protected void RaiseTopologyChanged()
    {
        // Shared properties can invalidate connections in another group. Clear every cache
        // before notifying views and snapshots, including those in sibling groups.
        GraphModel[] graphs = GetRootGraph().EnumerateGraphs().ToArray();
        foreach (GraphModel graph in graphs) graph._connectedInputProperties = null;
        foreach (GraphModel graph in graphs) graph.TopologyChanged?.Invoke(graph, EventArgs.Empty);
    }

    internal GraphModel GetRootGraph()
    {
        GraphModel root = this;
        var visited = new HashSet<GraphModel>(ReferenceEqualityComparer.Instance);
        while (visited.Add(root)
               && root.HierarchicalParent is GroupNode { HierarchicalParent: GraphModel parent })
            root = parent;
        return root;
    }

    /// <summary>Enumerates this model and its nested groups once per instance, in node order.</summary>
    public IEnumerable<GraphModel> EnumerateGraphs()
    {
        var visited = new HashSet<GraphModel>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<GraphModel>();
        pending.Push(this);
        while (pending.TryPop(out GraphModel? graph))
        {
            if (!visited.Add(graph)) continue;
            yield return graph;
            for (int i = graph.Nodes.Count - 1; i >= 0; i--)
            {
                if (graph.Nodes[i] is GroupNode group) pending.Push(group.Group.Value);
            }
        }
    }

    internal IEnumerable<IInputPort> EnumerateConnectedInputs()
        => GetRootGraph().EnumerateGraphs().SelectMany(graph => graph.Nodes)
            .SelectMany(node => node.GetConnectedInputs()).Distinct<IInputPort>(ReferenceEqualityComparer.Instance);

    internal bool HasAliasedInput(IInputPort input, IProperty property)
    {
        GraphModel root = GetRootGraph();
        if (root != this) return root.HasAliasedInput(input, property);
        if (_connectedInputProperties == null)
        {
            var properties = new Dictionary<IProperty, (IInputPort Port, int Count)>(ReferenceEqualityComparer.Instance);
            foreach (IInputPort connected in EnumerateConnectedInputs())
            {
                if (connected.Property?.GetEngineProperty() is not { } target
                    || connected.FindHierarchicalParent<GraphNode>() is not { } owner
                    || !owner.IsInputTargetAvailable(connected)) continue;
                properties[target] = properties.TryGetValue(target, out var previous)
                    ? (previous.Port, previous.Count + 1) : (connected, 1);
            }
            _connectedInputProperties = properties;
        }
        return _connectedInputProperties.TryGetValue(property, out var entry)
            && (entry.Count > 1 || entry.Port != input);
    }

    // For tests, which find a restored port by its Id.
    internal INodePort? FindNodePort(Guid id)
    {
        foreach (GraphNode node in Nodes.AsSpan())
        {
            foreach (INodeMember item in node.EnumerateMembers())
            {
                if (item is INodePort port
                    && port.Id.Value == id)
                {
                    return port;
                }
            }
        }

        return null;
    }
}
