namespace Beutl.NodeTree;

public sealed class SocketConnectionChangedEventArgs
{
    public SocketConnectionChangedEventArgs(Connection connection, bool isConnected)
    {
        Connection = connection;
        IsConnected = isConnected;
    }

    public Connection Connection { get; }

    public bool IsConnected { get; }
}
