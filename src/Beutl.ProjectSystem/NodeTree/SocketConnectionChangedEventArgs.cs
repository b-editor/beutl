namespace Beutl.NodeTree;

public sealed class SocketConnectionChangedEventArgs(Connection connection, bool isConnected)
{
    public Connection Connection { get; } = connection;

    public bool IsConnected { get; } = isConnected;
}
