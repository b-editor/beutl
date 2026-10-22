using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

public class ListInputPort<T> : NodePort<T>, IListInputPort
{
    public ListInputPort()
    {
        ScanProperties<ListInputPort<T>>();
        HideProperty(ConnectionsProperty);
        Connections.CollectionChanged += (_, _) =>
        {
            RaiseTopologyChanged();
            RaiseEdited();
        };
    }

    public Reference<Connection> Connection => default;

    [NotAutoSerialized]
    public IProperty<CoreList<Reference<Connection>>> ConnectionsProperty { get; } = new ValueOnlyProperty<CoreList<Reference<Connection>>>([]) { AffectsRender = false, KeepsList = true };

    // IListPort
    public CoreList<Reference<Connection>> Connections
    {
        get => ConnectionsProperty.Value;
        set => ConnectionsProperty.Value.Replace(value);
    }

    public override void NotifyConnected(Connection connection)
    {
        base.NotifyConnected(connection);
        if (ConnectionReferenceList.AddIfMissing(Connections, connection))
        {
            connection.Status = ConnectionStatus.Connected;
        }
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        if (ConnectionReferenceList.RemoveIfPresent(Connections, connection))
        {
            connection.Status = ConnectionStatus.Disconnected;
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
