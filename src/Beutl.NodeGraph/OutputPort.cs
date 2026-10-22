using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

public class OutputPort<T> : NodePort<T>, IOutputPort
{
    public OutputPort()
    {
        ScanProperties<OutputPort<T>>();
        HideProperty(ConnectionsProperty);
        Connections.CollectionChanged += (_, _) =>
        {
            RaiseTopologyChanged();
            RaiseEdited();
        };
    }

    [NotAutoSerialized]
    public IProperty<CoreList<Reference<Connection>>> ConnectionsProperty { get; } = new ValueOnlyProperty<CoreList<Reference<Connection>>>([]) { AffectsRender = false, KeepsList = true };

    public CoreList<Reference<Connection>> Connections
    {
        get => ConnectionsProperty.Value;
        set => ConnectionsProperty.Value.Replace(value);
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
