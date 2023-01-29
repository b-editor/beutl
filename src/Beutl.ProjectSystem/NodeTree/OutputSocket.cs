using Beutl.Collections;

namespace Beutl.NodeTree;

public class OutputSocket<T> : Socket<T>, IOutputSocket
{
    private readonly CoreList<IConnection> _connections = new();

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
}
