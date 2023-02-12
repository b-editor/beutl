namespace Beutl.NodeTree;

public interface IInputSocket : ISocket
{
    IConnection? Connection { get; }

    bool? IsValid { get; }

    void Receive(object? value);

    void NotifyConnected(IConnection connection);

    void NotifyDisconnected(IConnection connection);
}

public interface IInputSocket<T> : IInputSocket
{
    void Receive(T? value);
}
