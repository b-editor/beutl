using Beutl.Collections;
using Beutl.Editor;
using Beutl.Serialization;

namespace Beutl.NodeTree;

public class ListInputSocket<T> : Socket<T>, IListInputSocket
{
    public static readonly CoreProperty<CoreList<Reference<Connection>>> ConnectionsProperty;
    private readonly CoreList<Reference<Connection>> _connections = [];

    static ListInputSocket()
    {
        ConnectionsProperty =
            ConfigureProperty<CoreList<Reference<Connection>>, ListInputSocket<T>>(nameof(Connections))
                .Accessor(o => o.Connections, (o, v) => o.Connections = v)
                .Register();
    }

    public ListInputSocket()
    {
    }

    public Reference<Connection> Connection => default;

    // IListSocket
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
            connection.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Connected);
        }
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        if (Connections.Any(r => r.Id == connection.Id))
        {
            Connections.Remove(connection);
            connection.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Disconnected);
        }
    }

    public void MoveConnection(int oldIndex, int newIndex)
    {
        Connections.Move(oldIndex, newIndex);
    }

    // Receiveはno-op（値はCollectValuesで直接読み取り）
    public void Receive(object? value)
    {
    }

    // 評価時に全接続の値を収集
    public List<T?> CollectValues()
    {
        var result = new List<T?>(Connections.Count);
        foreach (Reference<Connection> conn in Connections)
        {
            if (conn is { Value.Output.Value: IOutputSocket output })
            {
                if (output.Value is T typed)
                {
                    result.Add(typed);
                    conn.Value.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Success);
                }
                else
                {
                    result.Add(default);
                    conn.Value.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Error);
                }
            }
        }

        return result;
    }

    public override void PreEvaluate(EvaluationContext context)
    {
        if (Connections.Count == 0)
        {
            base.PreEvaluate(context);
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
                context.Resolve(reference.Id, o => { Connections[index] = (Connection)o; });
            }
        }
    }
}
