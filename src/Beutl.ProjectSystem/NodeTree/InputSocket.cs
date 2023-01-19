namespace Beutl.NodeTree;

public class InputSocket<T> : Socket<T>, IInputSocket<T>
{
    public IConnection? Connection { get; private set; }

    public void NotifyConnected(IConnection connection)
    {
        Connection = connection;
        RaiseConnected(connection);
    }

    public void NotifyDisconnected(IConnection connection)
    {
        if (Connection == connection)
        {
            Connection = null;
            RaiseDisconnected(connection);
        }
    }

    public virtual void Receive(T? value)
    {
        Value = value;
    }

    public override void Evaluate(EvaluationContext context)
    {
        if (Connection == null)
        {
            base.Evaluate(context);
        }
    }
}
