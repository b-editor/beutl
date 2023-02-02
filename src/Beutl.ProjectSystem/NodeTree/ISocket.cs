using Beutl.Media;

namespace Beutl.NodeTree;

public interface ISocket : INodeItem
{
    event EventHandler<SocketConnectionChangedEventArgs>? Connected;
    event EventHandler<SocketConnectionChangedEventArgs>? Disconnected;

    Color Color { get; }

    Guid Id { get; }
}

public class Socket<T> : NodeItem<T>, ISocket
{
    public Color Color { get; }

    public event EventHandler<SocketConnectionChangedEventArgs>? Connected;

    public event EventHandler<SocketConnectionChangedEventArgs>? Disconnected;

    protected void RaiseConnected(IConnection connection)
    {
        Connected?.Invoke(this, new SocketConnectionChangedEventArgs(connection, true));
        InvalidateNodeTree();
    }

    protected void RaiseDisconnected(IConnection connection)
    {
        Disconnected?.Invoke(this, new SocketConnectionChangedEventArgs(connection, false));
        InvalidateNodeTree();
    }
}
