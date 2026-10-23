using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

public class ListInputPort<T> : NodePort<T>, IListInputPort
{
    public ListInputPort()
    {
        ScanProperties<ListInputPort<T>>();
        HideProperty(Connections);
        Connections.CollectionChanged += (_, _) =>
        {
            RaiseTopologyChanged();
            RaiseEdited();
        };
    }

    // A list port holds its connections in Connections, so its single connection stays empty.
    private readonly ValueOnlyProperty<Reference<Connection>> _connection = new(default) { AffectsRender = false };

    IProperty<Reference<Connection>> IInputPort.Connection => _connection;

    // IListPort
    [NotAutoSerialized]
    public IListProperty<Reference<Connection>> Connections { get; } = new ListProperty<Reference<Connection>> { AffectsRender = false };

    public override void NotifyConnected(Connection connection)
    {
        base.NotifyConnected(connection);
        if (ConnectionReferenceList.AddIfMissing(Connections, connection))
        {
            connection.Status.Value = ConnectionStatus.Connected;
        }
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        if (ConnectionReferenceList.RemoveIfPresent(Connections, connection))
        {
            connection.Status.Value = ConnectionStatus.Disconnected;
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
