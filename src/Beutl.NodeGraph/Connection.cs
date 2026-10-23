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

public sealed partial class Connection : Hierarchical
{
    public Connection(IInputPort input, IOutputPort output)
        : this()
    {
        Input.Value = new Reference<NodeMember>((NodeMember)input);
        Output.Value = new Reference<NodeMember>((NodeMember)output);
    }

    public Connection()
    {
        // Disconnected is a value rather than the default, as its CoreProperty form had no default.
        Status.Value = ConnectionStatus.Disconnected;
        ScanProperties<Connection>();
    }

    public IProperty<Reference<NodeMember>> Input { get; } = Property.CreateValue<Reference<NodeMember>>(default);

    public IProperty<Reference<NodeMember>> Output { get; } = Property.CreateValue<Reference<NodeMember>>(default);

    // Evaluation sets it again on every pass, so history leaves it out.
    [NotTracked]
    public IProperty<ConnectionStatus> Status { get; } = Property.CreateValue<ConnectionStatus>(default);

    public void Connect()
    {
        if (Output.Value.Value is not IOutputPort outputNodePort || Input.Value.Value is not IInputPort inputNodePort)
            throw new InvalidOperationException();
        outputNodePort.NotifyConnected(this);
        inputNodePort.NotifyConnected(this);
    }

    public void Disconnect()
    {
        if (Output.Value.Value is IOutputPort outputNodePort) outputNodePort.NotifyDisconnected(this);
        if (Input.Value.Value is IInputPort inputNodePort) inputNodePort.NotifyDisconnected(this);
        Status.Value = ConnectionStatus.Disconnected;
    }
}
