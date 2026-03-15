using Beutl.Collections;

namespace Beutl.NodeGraph;

public interface IOutputPort : INodePort
{
    CoreList<Reference<Connection>> Connections { get; }
}
