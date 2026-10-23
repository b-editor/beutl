namespace Beutl.NodeGraph;

public interface IOutputPort : INodePort
{
    IListProperty<Reference<Connection>> Connections { get; }
}
