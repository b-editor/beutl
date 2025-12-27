using Beutl.Collections;

namespace Beutl.NodeTree;

public interface IOutputSocket : ISocket
{
    CoreList<Connection> Connections { get; }

    bool TryConnect(IInputSocket socket);

    void Disconnect(IInputSocket socket);
}
