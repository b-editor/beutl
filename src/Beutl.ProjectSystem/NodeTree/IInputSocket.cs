using System.Globalization;

namespace Beutl.NodeTree;

public interface IInputSocket : ISocket
{
    IConnection? Connection { get; }

    void Receive(object? value);

    void NotifyConnected(IConnection connection);

    void NotifyDisconnected(IConnection connection);
}

public interface IInputSocket<T> : IInputSocket
{
    void Receive(T? value);

    void IInputSocket.Receive(object? value)
    {
        if (value is T t)
        {
            Receive(t);
        }
        else if (value == null)
        {
            Receive(default);
        }
        else
        {
            if (value is IConvertible convertible)
            {
                try
                {
                    if (convertible.ToType(typeof(T), CultureInfo.CurrentCulture) is T converted)
                    {
                        Receive(converted);
                    }
                }
                catch
                {
                }
            }

            Receive(default);
        }
    }
}
