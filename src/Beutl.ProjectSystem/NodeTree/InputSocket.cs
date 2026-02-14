using System.ComponentModel;

namespace Beutl.NodeTree;

public class InputSocket<T> : Socket<T>, IInputSocket
{
    public static readonly CoreProperty<Reference<Connection>> ConnectionProperty;

    static InputSocket()
    {
        ConnectionProperty = ConfigureProperty<Reference<Connection>, InputSocket<T>>(nameof(Connection))
            .Accessor(o => o.Connection, (o, v) => o.Connection = v)
            .Register();
    }

    public Reference<Connection> Connection
    {
        get;
        set => SetAndRaise(ConnectionProperty, ref field, value);
    }

    IObservable<Reference<Connection>> IInputSocket.GetConnectionObservable()
    {
        return this.GetObservable(ConnectionProperty);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if(args is CorePropertyChangedEventArgs coreArgs && coreArgs.Property.Id == ConnectionProperty.Id)
        {
            RaiseTopologyChanged();
            RaiseEdited();
        }
    }

    public override void NotifyConnected(Connection connection)
    {
        base.NotifyConnected(connection);
        if (!Connection.IsNull) throw new InvalidOperationException("This input socket is already connected.");
        Connection = connection;
        connection.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Connected);
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        if (Connection.IsNull || Connection.Id != connection.Id)
            throw new InvalidOperationException("This input socket is not connected to the specified connection.");
        Connection = default;
        connection.SetValue(Beutl.NodeTree.Connection.StatusProperty, ConnectionStatus.Disconnected);
    }
}
