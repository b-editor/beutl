using Beutl.Media;

namespace Beutl.NodeGraph;

public interface INodePort : INodeMember
{
    Color Color { get; }

    void NotifyConnected(Connection connection);

    void NotifyDisconnected(Connection connection);
}

public class NodePort<T> : NodeMember<T>, INodePort
{
    public Color Color { get; set; } = Colors.Teal;

    protected void VerifyConnection(Connection connection)
    {
        if (connection.Input.Id != Id && connection.Output.Id != Id)
        {
            throw new InvalidOperationException("The connection does not belong to this port.");
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
