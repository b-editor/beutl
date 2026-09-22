using Beutl.Collections;
using Beutl.Engine;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.NodeGraph;

[SuppressResourceClassGeneration]
public partial class GraphModel : EngineObject
{
    public static readonly CoreProperty<HierarchicalList<GraphNode>> NodesProperty;
    public static readonly CoreProperty<HierarchicalList<Connection>> AllConnectionsProperty;
    private readonly HierarchicalList<GraphNode> _nodes;
    private readonly HierarchicalList<Connection> _allConnections;
    private Dictionary<IProperty, (IInputPort Port, int Count)>? _connectedInputProperties;

    public event EventHandler? TopologyChanged;

    static GraphModel()
    {
        NodesProperty = ConfigureProperty<HierarchicalList<GraphNode>, GraphModel>(nameof(Nodes))
            .Accessor(o => o.Nodes, (o, v) => o.Nodes = v)
            .Register();

        AllConnectionsProperty = ConfigureProperty<HierarchicalList<Connection>, GraphModel>(nameof(AllConnections))
            .Accessor(o => o.AllConnections, (o, v) => o.AllConnections = v)
            .Register();
    }

    public GraphModel()
    {
        _nodes = new HierarchicalList<GraphNode>(this);
        _allConnections = new HierarchicalList<Connection>(this);
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
        if (obj is GroupNode group) group.Group.RaiseTopologyChanged();
        RaiseEdited();
    }

    private void OnNodeAttached(GraphNode obj)
    {
        obj.TopologyChanged += OnTopologyChanged;
        obj.Edited += OnNodeEdited;
        RaiseTopologyChanged();
        RaiseEdited();
    }

    public HierarchicalList<GraphNode> Nodes
    {
        get => _nodes;
        set => _nodes.Replace(value);
    }

    public HierarchicalList<Connection> AllConnections
    {
        get => _allConnections;
        set => _allConnections.Replace(value);
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

    private GraphModel GetRootGraph()
    {
        GraphModel root = this;
        while (root.HierarchicalParent is GroupNode { HierarchicalParent: GraphModel parent })
            root = parent;
        return root;
    }

    private IEnumerable<GraphModel> EnumerateGraphs()
    {
        yield return this;
        foreach (GroupNode group in Nodes.OfType<GroupNode>())
        {
            foreach (GraphModel graph in group.Group.EnumerateGraphs()) yield return graph;
        }
    }

    internal IEnumerable<IInputPort> EnumerateConnectedInputs()
        => GetRootGraph().EnumerateGraphs().SelectMany(graph => graph.Nodes)
            .SelectMany(node => node.GetConnectedInputs());

    internal bool HasAliasedInput(IInputPort input, IProperty property)
    {
        GraphModel root = GetRootGraph();
        if (root != this) return root.HasAliasedInput(input, property);
        if (_connectedInputProperties == null)
        {
            var properties = new Dictionary<IProperty, (IInputPort Port, int Count)>(ReferenceEqualityComparer.Instance);
            foreach (IInputPort connected in EnumerateConnectedInputs())
            {
                if (connected.Property?.GetEngineProperty() is not { } target) continue;
                properties[target] = properties.TryGetValue(target, out var previous)
                    ? (previous.Port, previous.Count + 1) : (connected, 1);
            }
            _connectedInputProperties = properties;
        }
        return _connectedInputProperties.TryGetValue(property, out var entry)
            && (entry.Count > 1 || entry.Port != input);
    }

    public INodePort? FindNodePort(Guid id)
    {
        foreach (GraphNode node in Nodes.GetMarshal().Value)
        {
            foreach (INodeMember item in node.EnumerateMembers())
            {
                if (item is INodePort port
                    && port.Id == id)
                {
                    return port;
                }
            }
        }

        return null;
    }
}
