using System.ComponentModel;

namespace Beutl.NodeGraph;

public class InputPort<T> : NodePort<T>, IInputPort
{
    public InputPort()
    {
        ScanProperties<InputPort<T>>();
        HideProperty(ConnectionProperty);
        ConnectionProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Connection)));
    }

    public IProperty<Reference<Connection>> ConnectionProperty { get; } = new ValueOnlyProperty<Reference<Connection>>(default) { AffectsRender = false };

    public Reference<Connection> Connection
    {
        get => ConnectionProperty.Value;
        set => ConnectionProperty.Value = value;
    }

    IObservable<Reference<Connection>> IInputPort.GetConnectionObservable()
    {
        return ConnectionProperty.GetObservable();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName == nameof(Connection))
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
        connection.Status = ConnectionStatus.Connected;
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        if (Connection.IsNull || Connection.Id != connection.Id)
            throw new InvalidOperationException("This input port is not connected to the specified connection.");
        Connection = default;
        connection.Status = ConnectionStatus.Disconnected;
    }
}
