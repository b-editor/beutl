using System.Diagnostics.CodeAnalysis;

namespace Beutl.NodeTree;

//IGroupInputOutputNode
public interface ISocketsCanBeAdded
{
    SocketLocation PossibleLocation { get; }

    bool AddSocket(ISocket socket, [NotNullWhen(true)] out IConnection? connection);
}
