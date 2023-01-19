namespace Beutl.NodeTree;

public sealed class SocketConnectionChangedEventArgs
{
    public SocketConnectionChangedEventArgs(IConnection connection, bool isConnected)
    {
        Connection = connection;
        IsConnected = isConnected;
    }

    public IConnection Connection { get; }

    public bool IsConnected { get; }
}
