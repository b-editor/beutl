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
        if (Connections.All(r => r.Id != connection.Id))
        {
            Connections.Add(connection);
        }
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        if (Connections.Any(r => r.Id == connection.Id))
        {
            Connections.Remove(connection);
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("Connections", Connections.Select(v => v.Id).ToArray());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        if (context.GetValue<List<Guid>>("Connections") is { } srcArray)
        {
            Connections.Replace(srcArray.Select(id => new Reference<Connection>(id)).ToArray());
            for (int i = 0; i < Connections.Count; i++)
            {
                int index = i;
                Reference<Connection> reference = Connections[i];
                context.Resolve(reference.Id, o =>
                {
                    Connections[index] = (Connection)o;
                });
            }
        }
    }
}
