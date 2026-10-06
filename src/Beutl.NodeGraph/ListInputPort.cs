using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

public class ListInputPort<T> : NodePort<T>, IListInputPort
{
    public static readonly CoreProperty<CoreList<Reference<Connection>>> ConnectionsProperty;
    private readonly CoreList<Reference<Connection>> _connections = [];

    static ListInputPort()
    {
        ConnectionsProperty =
            ConfigureProperty<CoreList<Reference<Connection>>, ListInputPort<T>>(nameof(Connections))
                .Accessor(o => o.Connections, (o, v) => o.Connections = v)
                .Register();
    }

    public ListInputPort()
    {
        Connections.CollectionChanged += (_, _) =>
        {
            RaiseTopologyChanged();
            RaiseEdited();
        };
    }

    public Reference<Connection> Connection => default;

    // IListPort
    [NotAutoSerialized]
    public CoreList<Reference<Connection>> Connections
    {
        get => _connections;
        set => _connections.Replace(value);
    }

    public override void NotifyConnected(Connection connection)
    {
        base.NotifyConnected(connection);
        if (ConnectionReferenceList.AddIfMissing(Connections, connection))
        {
            connection.SetValue(Beutl.NodeGraph.Connection.StatusProperty, ConnectionStatus.Connected);
        }
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        if (ConnectionReferenceList.RemoveIfPresent(Connections, connection))
        {
            connection.SetValue(Beutl.NodeGraph.Connection.StatusProperty, ConnectionStatus.Disconnected);
        }
    }

    public void MoveConnection(int oldIndex, int newIndex)
    {
        Connections.Move(oldIndex, newIndex);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        ConnectionReferenceList.Write(context, Connections);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        ConnectionReferenceList.Read(context, Connections);
    }
}
