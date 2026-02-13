namespace Beutl.NodeTree;

[Flags]
public enum ConnectionStatus
{
    Disconnected = 0b1,
    Connected = 0b10,

    Success = Connected | 0b100,
    Convert = Connected | 0b1000,
    Error = Connected | 0b10000,
}

public sealed class Connection : Hierarchical
{
    public static readonly CoreProperty<Reference<NodeItem>> InputProperty;
    public static readonly CoreProperty<Reference<NodeItem>> OutputProperty;
    public static readonly CoreProperty<ConnectionStatus> StatusProperty;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;

    static Connection()
    {
        InputProperty = ConfigureProperty<Reference<NodeItem>, Connection>(nameof(Input))
            .Accessor(o => o.Input, (o, v) => o.Input = v)
            .Register();
        OutputProperty = ConfigureProperty<Reference<NodeItem>, Connection>(nameof(Output))
            .Accessor(o => o.Output, (o, v) => o.Output = v)
            .Register();
        StatusProperty = ConfigureProperty<ConnectionStatus, Connection>(nameof(Status))
            .Accessor(o => o.Status, (o, v) => o.Status = v)
            .Register();
    }

    public Connection(IInputSocket input, IOutputSocket output)
    {
        Input = new Reference<NodeItem>((NodeItem)input);
        Output = new Reference<NodeItem>((NodeItem)output);
    }

    public Connection()
    {
    }

    public ConnectionStatus Status
    {
        get => _status;
        private set => SetAndRaise(StatusProperty, ref _status, value);
    }

    public Reference<NodeItem> Input
    {
        get;
        private set => SetAndRaise(InputProperty, ref field, value);
    }

    public Reference<NodeItem> Output
    {
        get;
        private set => SetAndRaise(OutputProperty, ref field, value);
    }

    public void Connect()
    {
        if (Output.Value is not IOutputSocket outputSocket || Input.Value is not IInputSocket inputSocket)
            throw new InvalidOperationException();
        outputSocket.NotifyConnected(this);
        inputSocket.NotifyConnected(this);
    }

    public void Disconnect()
    {
        if (Output.Value is not IOutputSocket outputSocket || Input.Value is not IInputSocket inputSocket)
            throw new InvalidOperationException();
        outputSocket.NotifyDisconnected(this);
        inputSocket.NotifyDisconnected(this);
    }
}
