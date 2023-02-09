using System.Text.Json.Nodes;

using Beutl.Collections;

namespace Beutl.NodeTree;

public class OutputSocket<T> : Socket<T>, IOutputSocket
{
    private readonly CoreList<IConnection> _connections = new();
    private List<Guid>? _inputIds = null;

    public ICoreReadOnlyList<IConnection> Connections => _connections;

    public void Disconnect(IInputSocket socket)
    {
        if (_connections.FirstOrDefault(x => x.Input == socket) is { } connection)
        {
            _connections.Remove(connection);
            RaiseDisconnected(connection);
            socket.NotifyDisconnected(connection);
        }
    }

    public bool TryConnect(IInputSocket socket)
    {
        if (TryConnectCore(socket))
        {
            if (_inputIds?.Contains(socket.Id) == true)
            {
                _inputIds.Remove(socket.Id);

                if (_inputIds.Count == 0)
                {
                    _inputIds = null;
                }
            }

            return true;
        }
        else
        {
            return false;
        }
    }

    private bool TryConnectCore(IInputSocket socket)
    {
        if (_connections.Any(x => x.Input == socket)
            || socket.Connection != null)
            return false;

        var connection = new Connection(socket, this);
        _connections.Add(connection);
        RaiseConnected(connection);
        socket.NotifyConnected(connection);
        return true;
    }

    public override void Evaluate(EvaluationContext context)
    {
        base.Evaluate(context);
        foreach (IConnection item in _connections.GetMarshal().Value)
        {
            item.Input.Receive(Value);
        }
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("connection-inputs", out var srcNode)
                && srcNode is JsonArray srcArray)
            {
                if (_inputIds != null)
                {
                    _inputIds.Clear();
                    _inputIds.EnsureCapacity(srcArray.Count);
                }
                else
                {
                    _inputIds = new(srcArray.Count);
                }

                foreach (JsonNode? item in srcArray)
                {
                    if (item is JsonValue itemv
                        && itemv.TryGetValue(out Guid id))
                    {
                        _inputIds.Add(id);
                    }
                }

                TryRestoreConnection();
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);

        if (_connections.Count > 0)
        {
            var array = new JsonArray();
            foreach (IConnection item in _connections)
            {
                array.Add(item.Input.Id);
            }

            json["connection-inputs"] = array;
        }
    }

    private void TryRestoreConnection()
    {
        if (_inputIds != null)
        {
            for (int i = _inputIds.Count - 1; i >= 0; i--)
            {
                ISocket? socket = NodeTree?.FindSocket(_inputIds[i]);
                if (socket is IInputSocket inputSocket)
                {
                    if (TryConnectCore(inputSocket))
                    {
                        _inputIds.RemoveAt(i);
                    }
                }
            }

            if (_inputIds.Count == 0)
            {
                _inputIds = null;
            }
        }
    }

    protected override void OnAttachedToNodeTree(NodeTreeSpace nodeTree)
    {
        base.OnAttachedToNodeTree(nodeTree);
        TryRestoreConnection();
    }

    protected override void OnDetachedFromNodeTree(NodeTreeSpace nodeTree)
    {
        base.OnDetachedFromNodeTree(nodeTree);
        if (_inputIds != null)
        {
            _inputIds.Clear();
            _inputIds.EnsureCapacity(_connections.Count);
        }
        else
        {
            _inputIds = new(_connections.Count);
        }

        for (int i = _connections.Count - 1; i >= 0; i--)
        {
            IConnection item = _connections[i];

            _inputIds.Add(item.Input.Id);

            _connections.RemoveAt(i);
            RaiseDisconnected(item);
            item.Input.NotifyDisconnected(item);
        }
    }
}
