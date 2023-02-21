using Beutl.Media;

namespace Beutl.NodeTree;

public interface ISocket : INodeItem
{
    event EventHandler<SocketConnectionChangedEventArgs>? Connected;
    event EventHandler<SocketConnectionChangedEventArgs>? Disconnected;

    Color Color { get; }
}

public class Socket<T> : NodeItem<T>, ISocket
{
    public Color Color { get; set; } = Colors.Teal;

    public event EventHandler<SocketConnectionChangedEventArgs>? Connected;

    public event EventHandler<SocketConnectionChangedEventArgs>? Disconnected;

    protected void RaiseConnected(Connection connection)
    {
        Connected?.Invoke(this, new SocketConnectionChangedEventArgs(connection, true));
        InvalidateNodeTree();
    }

    protected void RaiseDisconnected(Connection connection)
    {
        Disconnected?.Invoke(this, new SocketConnectionChangedEventArgs(connection, false));
        InvalidateNodeTree();
    }
}
