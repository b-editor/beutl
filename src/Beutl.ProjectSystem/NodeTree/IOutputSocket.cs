using Beutl.Collections;

namespace Beutl.NodeTree;

public interface IOutputSocket : ISocket
{
    ICoreReadOnlyList<Connection> Connections { get; }

    bool TryConnect(IInputSocket socket);

    void Disconnect(IInputSocket socket);
}
