namespace Beutl.NodeGraph;

public interface IInputPort : INodePort
{
    Reference<Connection> Connection { get; }

    internal IObservable<Reference<Connection>> GetConnectionObservable()
    {
        throw new InvalidOperationException();
    }
}
