namespace Beutl.NodeTree;

public interface IInputSocket : ISocket
{
    Reference<Connection> Connection { get; }

    internal IObservable<Reference<Connection>> GetConnectionObservable()
    {
        throw new InvalidOperationException();
    }
}
