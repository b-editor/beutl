using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

public class OutputPort<T> : NodePort<T>, IOutputPort
{
    public static readonly CoreProperty<CoreList<Reference<Connection>>> ConnectionsProperty;
    private readonly CoreList<Reference<Connection>> _connections = [];

    static OutputPort()
    {
        ConnectionsProperty = ConfigureProperty<CoreList<Reference<Connection>>, OutputPort<T>>(nameof(Connections))
            .Accessor(o => o.Connections, (o, v) => o.Connections = v)
            .Register();
    }

    public OutputPort()
    {
        Connections.CollectionChanged += (_, _) =>
        {
            RaiseTopologyChanged();
            RaiseEdited();
        };
    }

    [NotAutoSerialized]
    public CoreList<Reference<Connection>> Connections
    {
        get => _connections;
        set => _connections.Replace(value);
    }

    public override void NotifyConnected(Connection connection)
    {
        base.NotifyConnected(connection);
        ConnectionReferenceList.AddIfMissing(Connections, connection);
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        ConnectionReferenceList.RemoveIfPresent(Connections, connection);
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
