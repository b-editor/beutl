namespace Beutl.NodeTree;

public interface IInputSocket : ISocket
{
    Connection? Connection { get; }

    bool? IsValid { get; }

    void Receive(object? value);

    void NotifyConnected(Connection connection);

    void NotifyDisconnected(Connection connection);
}
