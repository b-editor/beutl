using Beutl.Collections;

namespace Beutl.NodeTree;

public interface IListSocket : ISocket
{
    CoreList<Connection> ListConnections { get; }

    void MoveConnection(int oldIndex, int newIndex);
}
