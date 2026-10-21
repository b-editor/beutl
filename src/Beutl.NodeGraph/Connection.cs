using System.ComponentModel;

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
    public Connection(IInputPort input, IOutputPort output)
        : this()
    {
        InputProperty.Value = new Reference<NodeMember>((NodeMember)input);
        OutputProperty.Value = new Reference<NodeMember>((NodeMember)output);
    }

    public Connection()
    {
        // Disconnected is a value rather than the default, as its CoreProperty form had no default.
        StatusProperty.Value = ConnectionStatus.Disconnected;
        ScanProperties<Connection>();
        InputProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Input)));
        OutputProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Output)));
        StatusProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Status)));
    }

    public IProperty<Reference<NodeMember>> InputProperty { get; } = new ValueOnlyProperty<Reference<NodeMember>>(default);

    public IProperty<Reference<NodeMember>> OutputProperty { get; } = new ValueOnlyProperty<Reference<NodeMember>>(default);

    // Evaluation sets it again on every pass, so history leaves it out.
    [NotTracked]
    public IProperty<ConnectionStatus> StatusProperty { get; } = new ValueOnlyProperty<ConnectionStatus>(default);

    public ConnectionStatus Status
    {
        get => StatusProperty.Value;
        set => StatusProperty.Value = value;
    }

    public Reference<NodeMember> Input => InputProperty.Value;

    public Reference<NodeMember> Output => OutputProperty.Value;

    public void Connect()
    {
        if (Output.Value is not IOutputPort outputNodePort || Input.Value is not IInputPort inputNodePort)
            throw new InvalidOperationException();
        outputNodePort.NotifyConnected(this);
        inputNodePort.NotifyConnected(this);
    }

    public void Disconnect()
    {
        if (Output.Value is IOutputPort outputNodePort) outputNodePort.NotifyDisconnected(this);
        if (Input.Value is IInputPort inputNodePort) inputNodePort.NotifyDisconnected(this);
        Status = ConnectionStatus.Disconnected;
    }
}
