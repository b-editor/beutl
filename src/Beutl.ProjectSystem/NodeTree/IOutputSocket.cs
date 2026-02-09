using Beutl.Collections;

namespace Beutl.NodeTree;

public interface IOutputSocket : ISocket
{
    CoreList<Reference<Connection>> Connections { get; }
}
