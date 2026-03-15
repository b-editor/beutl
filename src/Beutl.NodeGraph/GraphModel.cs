using Beutl.Collections;
using Beutl.Engine;

namespace Beutl.NodeGraph;

[SuppressResourceClassGeneration]
public class GraphModel : EngineObject
{
    public static readonly CoreProperty<HierarchicalList<GraphNode>> NodesProperty;
    public static readonly CoreProperty<HierarchicalList<Connection>> AllConnectionsProperty;
    private readonly HierarchicalList<GraphNode> _nodes;
    private readonly HierarchicalList<Connection> _allConnections;

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
        RaiseEdited();
    }

    private void OnNodeAttached(GraphNode obj)
    {
        obj.TopologyChanged += OnTopologyChanged;
        obj.Edited += OnNodeEdited;
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
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }

    public INodePort? FindNodePort(Guid id)
    {
        foreach (GraphNode node in Nodes.GetMarshal().Value)
        {
            foreach (INodeMember item in node.Items.GetMarshal().Value)
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
