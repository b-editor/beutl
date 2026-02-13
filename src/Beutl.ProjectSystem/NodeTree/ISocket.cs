using Beutl.Media;

namespace Beutl.NodeTree;

public interface ISocket : INodeItem
{
    Color Color { get; }

    void NotifyConnected(Connection connection);

    void NotifyDisconnected(Connection connection);
}

public class Socket<T> : NodeItem<T>, ISocket
{
    public Color Color { get; set; } = Colors.Teal;

    protected void VerifyConnection(Connection connection)
    {
        // InputもOutputも自分じゃない場合は例外を発生させる
        if (connection.Input.Id != Id && connection.Output.Id != Id)
        {
            throw new InvalidOperationException("The connection does not belong to this socket.");
        }
    }

    public virtual void NotifyConnected(Connection connection)
    {
        VerifyConnection(connection);
    }

    public virtual void NotifyDisconnected(Connection connection)
    {
        VerifyConnection(connection);
    }
}
