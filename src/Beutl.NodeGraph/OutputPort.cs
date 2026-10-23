using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

public class OutputPort<T> : NodePort<T>, IOutputPort
{
    public OutputPort()
    {
        ScanProperties<OutputPort<T>>();
        HideProperty(Connections);
        Connections.CollectionChanged += (_, _) =>
        {
            RaiseTopologyChanged();
            RaiseEdited();
        };
    }

    [NotAutoSerialized]
    public IListProperty<Reference<Connection>> Connections { get; } = new ListProperty<Reference<Connection>> { AffectsRender = false };

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
