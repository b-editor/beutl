using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.NodeTree;

public class ListOutputSocket<T> : Socket<T>, IListOutputSocket
{
    private HashSet<Guid>? _unresolvedInputIds;
    // デシリアライズ時の元の順序を保持（挿入位置の計算に使用）
    private List<Guid>? _inputIds;
    private readonly List<T?> _values = [];
    private UnsafeBox<T> _box;

    public ListOutputSocket()
    {
        Connections.Attached += OnConnectionAttached;
        Connections.Detached += OnConnectionDetached;
    }

    ~ListOutputSocket()
    {
        _box.Dispose();
    }

    // IOutputSocket
    public CoreList<Connection> Connections { get; } = [];

    // IListSocket
    public CoreList<Connection> ListConnections => Connections;

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

    public bool TryConnect(IInputSocket socket)
    {
        if (Connections.Any(x => x.Input == socket)
            || socket.Connection != null)
            return false;

        var connection = new Connection(socket, this);

        int insertIndex = GetInsertionIndex(socket.Id);
        Connections.Insert(insertIndex, connection);

        if (_unresolvedInputIds?.Contains(socket.Id) == true)
        {
            _unresolvedInputIds.Remove(socket.Id);
            if (_unresolvedInputIds.Count == 0)
            {
                _unresolvedInputIds = null;
                _inputIds = null;
            }
        }

        return true;
    }

    public void Disconnect(IInputSocket socket)
    {
        if (Connections.FirstOrDefault(x => x.Input == socket) is { } connection)
        {
            Connections.Remove(connection);
        }
    }

    private void OnConnectionAttached(Connection obj)
    {
        RaiseConnected(obj);
        obj.Input.NotifyConnected(obj);
    }

    private void OnConnectionDetached(Connection obj)
    {
        RaiseDisconnected(obj);
        obj.Input.NotifyDisconnected(obj);
    }

    // PostEvaluate: 各接続にインデックス対応の値を送信
    public override void PostEvaluate(EvaluationContext context)
    {
        base.PostEvaluate(context);
        for (int i = 0; i < Connections.Count; i++)
        {
            T? value = i < _values.Count ? _values[i] : default;
            Connection conn = Connections[i];
            if (conn.Input is InputSocket<T> sameType)
            {
                sameType.Receive(value);
            }
            else
            {
                _box.Update(value);
                conn.Input.Receive(_box.Object);
            }
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("connection-inputs", Connections.Select(v => v.Input.Id).ToArray());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        if (context.GetValue<List<Guid>>("connection-inputs") is { } srcArray)
        {
            _unresolvedInputIds = new HashSet<Guid>(srcArray);
            _inputIds = srcArray;
            TryRestoreConnections();
        }
    }

    private void TryRestoreConnections()
    {
        if (_inputIds != null)
        {
            foreach (Guid inputId in _inputIds)
            {
                ISocket? socket = NodeTree?.FindSocket(inputId);
                if (socket is IInputSocket inputSocket)
                {
                    if (Connections.Any(x => x.Input == inputSocket)
                        || inputSocket.Connection != null)
                        continue;

                    var connection = new Connection(inputSocket, this);
                    int insertIndex = GetInsertionIndex(inputSocket.Id);
                    Connections.Insert(insertIndex, connection);

                    if (_unresolvedInputIds?.Contains(inputSocket.Id) == true)
                    {
                        _unresolvedInputIds.Remove(inputSocket.Id);
                        if (_unresolvedInputIds.Count == 0)
                        {
                            _unresolvedInputIds = null;
                        }
                    }
                }
            }
        }
    }

    private int GetInsertionIndex(Guid inputId)
    {
        if (_inputIds == null)
            return Connections.Count;

        int targetOrder = _inputIds.IndexOf(inputId);
        if (targetOrder < 0)
            return Connections.Count;

        // 元の順序で、自分より後にあるべき接続の前に挿入
        for (int i = 0; i < Connections.Count; i++)
        {
            int existingOrder = _inputIds.IndexOf(Connections[i].Input.Id);
            if (existingOrder < 0 || existingOrder > targetOrder)
                return i;
        }

        return Connections.Count;
    }

    protected override void OnAttachedToNodeTree(NodeTreeModel nodeTree)
    {
        base.OnAttachedToNodeTree(nodeTree);
        TryRestoreConnections();
    }

    protected override void OnDetachedFromNodeTree(NodeTreeModel nodeTree)
    {
        base.OnDetachedFromNodeTree(nodeTree);
        _inputIds = null;

        if (_unresolvedInputIds != null)
        {
            _unresolvedInputIds.Clear();
            _unresolvedInputIds.EnsureCapacity(Connections.Count);
        }
        else
        {
            _unresolvedInputIds = new(Connections.Count);
        }

        for (int i = Connections.Count - 1; i >= 0; i--)
        {
            Connection item = Connections[i];
            _unresolvedInputIds.Add(item.Input.Id);
            Connections.RemoveAt(i);
        }

        _inputIds = new (_unresolvedInputIds);
    }
}
