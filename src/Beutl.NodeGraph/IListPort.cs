namespace Beutl.NodeGraph;

public interface IListPort : INodePort
{
    IListProperty<Reference<Connection>> Connections { get; }

    void MoveConnection(int oldIndex, int newIndex);
}
