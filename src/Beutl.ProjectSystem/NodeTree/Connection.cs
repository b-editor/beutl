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

public sealed class Connection : CoreObject
{
    public static readonly CoreProperty<ConnectionStatus> StatusProperty;
    private ConnectionStatus _status;

    static Connection()
    {
        StatusProperty = ConfigureProperty<ConnectionStatus, Connection>(nameof(Status))
            .Accessor(o => o.Status, (o, v) => o.Status = v)
            .Register();
    }

    public Connection(IInputSocket input, IOutputSocket output)
    {
        Input = input;
        Output = output;
    }

    public ConnectionStatus Status
    {
        get => _status;
        private set => SetAndRaise(StatusProperty, ref _status, value);
    }

    public IInputSocket Input { get; }

    public IOutputSocket Output { get; }
}
