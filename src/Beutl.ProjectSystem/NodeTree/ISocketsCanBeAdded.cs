namespace Beutl.NodeTree;

//IGroupInputOutputNode
public interface ISocketsCanBeAdded
{
    SocketLocation PossibleLocation { get; }

    bool AddSocket(ISocket socket, out IConnection? connection);
}
