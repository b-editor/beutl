using Beutl.Collections;

namespace Beutl.NodeTree;

public interface IListSocket : ISocket
{
    CoreList<Reference<Connection>> Connections { get; }

    void MoveConnection(int oldIndex, int newIndex);
}
