namespace Beutl.NodeGraph;

public class InputPort<T> : NodePort<T>, IInputPort
{
    public InputPort()
    {
        ScanProperties<InputPort<T>>();
        HideProperty(Connection);
        Connection.ValueChanged += (_, _) => OnConnectionChanged();
    }

    public IProperty<Reference<Connection>> Connection { get; } = new ValueOnlyProperty<Reference<Connection>>(default) { AffectsRender = false };

    IObservable<Reference<Connection>> IInputPort.GetConnectionObservable()
    {
        return Connection.GetObservable();
    }

    private protected virtual void OnConnectionChanged()
    {
        RaiseTopologyChanged();
        RaiseEdited();
    }

    public override void NotifyConnected(Connection connection)
    {
        base.NotifyConnected(connection);
        if (!Connection.Value.IsNull) throw new InvalidOperationException("This input port is already connected.");
        Connection.Value = connection;
        connection.Status.Value = ConnectionStatus.Connected;
    }

    public override void NotifyDisconnected(Connection connection)
    {
        base.NotifyDisconnected(connection);
        if (Connection.Value.IsNull || Connection.Value.Id != connection.Id.Value)
            throw new InvalidOperationException("This input port is not connected to the specified connection.");
        Connection.Value = default;
        connection.Status.Value = ConnectionStatus.Disconnected;
    }
}
