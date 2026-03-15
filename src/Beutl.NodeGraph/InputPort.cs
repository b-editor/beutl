using System.ComponentModel;

namespace Beutl.NodeGraph;

public class InputPort<T> : NodePort<T>, IInputPort
{
    public static readonly CoreProperty<Reference<Connection>> ConnectionProperty;

    static InputPort()
    {
        ConnectionProperty = ConfigureProperty<Reference<Connection>, InputPort<T>>(nameof(Connection))
            .Accessor(o => o.Connection, (o, v) => o.Connection = v)
            .Register();
    }

    public Reference<Connection> Connection
    {
        get;
        set => SetAndRaise(ConnectionProperty, ref field, value);
    }

    IObservable<Reference<Connection>> IInputPort.GetConnectionObservable()
    {
        return this.GetObservable(ConnectionProperty);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs coreArgs && coreArgs.Property.Id == ConnectionProperty.Id)
        {
            RaiseTopologyChanged();
            RaiseEdited();
        }
    }

    public override void NotifyConnected(Connection connection)
    {
        base.NotifyConnected(connection);
        if (!Connection.IsNull) throw new InvalidOperationException("This input port is already connected.");
        Connection = connection;
        connection.SetValue(Beutl.NodeGraph.Connection.StatusProperty, ConnectionStatus.Connected);
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        if (Connection.IsNull || Connection.Id != connection.Id)
            throw new InvalidOperationException("This input port is not connected to the specified connection.");
        Connection = default;
        connection.SetValue(Beutl.NodeGraph.Connection.StatusProperty, ConnectionStatus.Disconnected);
    }
}
