using Beutl.Collections;

namespace Beutl.NodeGraph;

public interface IListPort : INodePort
{
    CoreList<Reference<Connection>> Connections { get; }

    void MoveConnection(int oldIndex, int newIndex);
}
