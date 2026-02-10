using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.NodeTree;

public class ListOutputSocket<T> : Socket<T>, IListOutputSocket
{
    public static readonly CoreProperty<CoreList<Reference<Connection>>> ConnectionsProperty;
    private readonly CoreList<Reference<Connection>> _connections = [];
    private readonly List<T?> _values = [];
    private UnsafeBox<T> _box;

    static ListOutputSocket()
    {
        ConnectionsProperty =
            ConfigureProperty<CoreList<Reference<Connection>>, ListOutputSocket<T>>(nameof(Connections))
                .Accessor(o => o.Connections, (o, v) => o.Connections = v)
                .Register();
    }

    public ListOutputSocket()
    {
        Connections.CollectionChanged += (_, _) =>
        {
            RaiseTopologyChanged();
            RaiseEdited(EventArgs.Empty);
        };
    }

    ~ListOutputSocket()
    {
        _box.Dispose();
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

    public void MoveConnection(int oldIndex, int newIndex)
    {
        Connections.Move(oldIndex, newIndex);
    }

    // ノードのEvaluateで呼ばれる - 出力する値のリストを設定
    public void SetValues(IReadOnlyList<T?> values)
    {
        _values.Clear();
        _values.AddRange(values);
    }

    // PostEvaluate: 各接続にインデックス対応の値を送信
    public override void PostEvaluate(EvaluationContext context)
    {
        base.PostEvaluate(context);
        for (int i = 0; i < Connections.Count; i++)
        {
            T? value = i < _values.Count ? _values[i] : default;
            Connection? conn = Connections[i];
            if (conn == null) continue;
            if (conn.Input.Value is InputSocket<T> sameType)
            {
                sameType.Receive(value);
            }
            else if (conn.Input.Value is IInputSocket inputSocket)
            {
                _box.Update(value);
                inputSocket.Receive(_box.Object);
            }
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
                context.Resolve(reference.Id, o => Connections[index] = (Connection)o);
            }
        }
    }
}
