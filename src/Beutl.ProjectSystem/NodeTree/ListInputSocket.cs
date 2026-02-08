using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.NodeTree;

public class ListInputSocket<T> : Socket<T>, IListInputSocket
{
    private HashSet<Guid>? _unresolvedOutputIds;

    // デシリアライズ時の元の順序を保持（挿入位置の計算に使用）
    private List<Guid>? _outputIds;

    public Connection? Connection => null;

    // IListSocket
    public CoreList<Connection> ListConnections { get; } = [];

    public void MoveConnection(int oldIndex, int newIndex)
    {
        ListConnections.Move(oldIndex, newIndex);
    }

    public void NotifyConnected(Connection connection)
    {
        if (_unresolvedOutputIds?.Contains(connection.Output.Id) == true)
        {
            _unresolvedOutputIds.Remove(connection.Output.Id);
            if (_unresolvedOutputIds.Count == 0)
            {
                _unresolvedOutputIds = null;
                _outputIds = null;
            }
        }

        int insertIndex = GetInsertionIndex(connection.Output.Id);
        ListConnections.Insert(insertIndex, connection);
        connection.SetValue(Connection.StatusProperty, ConnectionStatus.Success);
        RaiseConnected(connection);
    }

    public void NotifyDisconnected(Connection connection)
    {
        ListConnections.Remove(connection);
        RaiseDisconnected(connection);
    }

    // Receiveはno-op（値はCollectValuesで直接読み取り）
    public void Receive(object? value)
    {
    }

    // 評価時に全接続の値を収集
    public List<T?> CollectValues()
    {
        var result = new List<T?>(ListConnections.Count);
        foreach (Connection conn in ListConnections)
        {
            result.Add(conn.Output.Value is T typed ? typed : default);
        }

        return result;
    }

    public override void PreEvaluate(EvaluationContext context)
    {
        if (ListConnections.Count == 0)
        {
            base.PreEvaluate(context);
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue("connection-outputs", ListConnections.Select(v => v.Output.Id).ToArray());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        if (context.GetValue<List<Guid>>("connection-outputs") is { } srcArray)
        {
            _unresolvedOutputIds = new HashSet<Guid>(srcArray);
            _outputIds = srcArray;
            TryRestoreConnections();
        }
    }

    private void TryRestoreConnections()
    {
        if (_outputIds != null)
        {
            foreach (Guid outputId in _outputIds)
            {
                ISocket? socket = NodeTree?.FindSocket(outputId);
                if (socket is IOutputSocket outputSocket)
                {
                    outputSocket.TryConnect(this);
                }
            }
        }
    }

    private int GetInsertionIndex(Guid outputId)
    {
        if (_outputIds == null)
            return ListConnections.Count;

        int targetOrder = _outputIds.IndexOf(outputId);
        if (targetOrder < 0)
            return ListConnections.Count;

        // 元の順序で、自分より後にあるべき接続の前に挿入
        for (int i = 0; i < ListConnections.Count; i++)
        {
            int existingOrder = _outputIds.IndexOf(ListConnections[i].Output.Id);
            if (existingOrder < 0 || existingOrder > targetOrder)
                return i;
        }

        return ListConnections.Count;
    }

    protected override void OnAttachedToNodeTree(NodeTreeModel nodeTree)
    {
        base.OnAttachedToNodeTree(nodeTree);
        TryRestoreConnections();
    }

    protected override void OnDetachedFromNodeTree(NodeTreeModel nodeTree)
    {
        base.OnDetachedFromNodeTree(nodeTree);
        _outputIds = null;

        if (_unresolvedOutputIds != null)
        {
            _unresolvedOutputIds.Clear();
            _unresolvedOutputIds.EnsureCapacity(ListConnections.Count);
        }
        else
        {
            _unresolvedOutputIds = new(ListConnections.Count);
        }

        for (int i = ListConnections.Count - 1; i >= 0; i--)
        {
            Connection item = ListConnections[i];
            _unresolvedOutputIds.Add(item.Output.Id);
            ListConnections.RemoveAt(i);
        }

        _outputIds = new(_unresolvedOutputIds);
    }
}
