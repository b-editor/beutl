namespace Beutl.NodeGraph;

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
    public static readonly CoreProperty<Reference<NodeMember>> InputProperty;
    public static readonly CoreProperty<Reference<NodeMember>> OutputProperty;
    public static readonly CoreProperty<ConnectionStatus> StatusProperty;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;

    static Connection()
    {
        InputProperty = ConfigureProperty<Reference<NodeMember>, Connection>(nameof(Input))
            .Accessor(o => o.Input, (o, v) => o.Input = v)
            .Register();
        OutputProperty = ConfigureProperty<Reference<NodeMember>, Connection>(nameof(Output))
            .Accessor(o => o.Output, (o, v) => o.Output = v)
            .Register();
        StatusProperty = ConfigureProperty<ConnectionStatus, Connection>(nameof(Status))
            .Accessor(o => o.Status, (o, v) => o.Status = v)
            .Register();
    }

    public Connection(IInputPort input, IOutputPort output)
    {
        Input = new Reference<NodeMember>((NodeMember)input);
        Output = new Reference<NodeMember>((NodeMember)output);
    }

    public Connection()
    {
    }

    public ConnectionStatus Status
    {
        get => _status;
        set => SetAndRaise(StatusProperty, ref _status, value);
    }

    public Reference<NodeMember> Input
    {
        get;
        private set => SetAndRaise(InputProperty, ref field, value);
    }

    public Reference<NodeMember> Output
    {
        get;
        private set => SetAndRaise(OutputProperty, ref field, value);
    }

    public void Connect()
    {
        if (Output.Value is not IOutputPort outputNodePort || Input.Value is not IInputPort inputNodePort)
            throw new InvalidOperationException();
        outputNodePort.NotifyConnected(this);
        inputNodePort.NotifyConnected(this);
    }

    public void Disconnect()
    {
        if (Output.Value is not IOutputPort outputNodePort || Input.Value is not IInputPort inputNodePort)
            throw new InvalidOperationException();
        outputNodePort.NotifyDisconnected(this);
        inputNodePort.NotifyDisconnected(this);
    }
}
