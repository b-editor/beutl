namespace Beutl.NodeGraph;

public interface IInputPort : INodePort
{
    IProperty<Reference<Connection>> Connection { get; }

    internal IObservable<Reference<Connection>> GetConnectionObservable()
    {
        throw new InvalidOperationException();
    }
}
