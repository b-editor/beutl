namespace Beutl.NodeTree;

public interface IInputSocket : ISocket
{
    Reference<Connection> Connection { get; }

    void Receive(object? value);

    internal IObservable<Reference<Connection>> GetConnectionObservable()
    {
        throw new InvalidOperationException();
    }
}
