using System.Text.Json.Nodes;
using Avalonia.Input;
using Beutl.Collections;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.NodeTree;

public abstract class NodeTreeModel : Hierarchical, INotifyEdited
{
    public static readonly CoreProperty<HierarchicalList<Node>> NodesProperty;
    public static readonly CoreProperty<HierarchicalList<Connection>> AllConnectionsProperty;
    private readonly HierarchicalList<Node> _nodes;
    private readonly HierarchicalList<Connection> _allConnections;

    public event EventHandler? Edited;

    static NodeTreeModel()
    {
        NodesProperty = ConfigureProperty<HierarchicalList<Node>, NodeTreeModel>(nameof(Nodes))
            .Accessor(o => o.Nodes, (o, v) => o.Nodes = v)
            .Register();

        AllConnectionsProperty = ConfigureProperty<HierarchicalList<Connection>, NodeTreeModel>(nameof(AllConnections))
            .Accessor(o => o.AllConnections, (o, v) => o.AllConnections = v)
            .Register();
    }

    public NodeTreeModel()
    {
        _nodes = new HierarchicalList<Node>(this);
        _allConnections = new HierarchicalList<Connection>(this);
    }

    public HierarchicalList<Node> Nodes
    {
        get => _nodes;
        set => _nodes.Replace(value);
    }

    public HierarchicalList<Connection> AllConnections
    {
        get => _allConnections;
        set => _allConnections.Replace(value);
    }

    public Connection Connect(IInputSocket inputSocket, IOutputSocket outputSocket)
    {
        var connection = new Connection(inputSocket, outputSocket);
        connection.Connect();
        AllConnections.Add(connection);
        return connection;
    }

    public void Disconnect(Connection connection)
    {
        AllConnections.Remove(connection);
        connection.Disconnect();
    }

    protected void RaiseInvalidated(EventArgs args)
    {
        Edited?.Invoke(this, args);
    }

    public ISocket? FindSocket(Guid id)
    {
        foreach (Node node in Nodes.GetMarshal().Value)
        {
            foreach (INodeItem item in node.Items.GetMarshal().Value)
            {
                if (item is ISocket socket
                    && socket.Id == id)
                {
                    return socket;
                }
            }
        }

        return null;
    }
}
