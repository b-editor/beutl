using Beutl.Collections;

namespace Beutl.NodeTree;

public interface IOutputSocket : ISocket
{
    ICoreReadOnlyList<IConnection> Connections { get; }

    bool TryConnect(IInputSocket socket);

    void Disconnect(IInputSocket socket);
}

public interface IOutputSocket<T> : IOutputSocket
{
    bool TryConnect(IInputSocket<T> socket);

    void Disconnect(IInputSocket<T> socket);

    bool IOutputSocket.TryConnect(IInputSocket socket)
    {
        if (socket is IInputSocket<T> tsocket)
        {
            return TryConnect(tsocket);
        }
        else
        {
            return false;
        }
    }

    void IOutputSocket.Disconnect(IInputSocket socket)
    {
        if(socket is IInputSocket<T> tsocket)
        {
            Disconnect(tsocket);
        }
    }
}
