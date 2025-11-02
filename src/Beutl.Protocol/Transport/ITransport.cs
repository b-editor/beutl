using Beutl.Protocol.Operations;

namespace Beutl.Protocol.Transport;

/// <summary>
/// Represents a transport layer for synchronizing operations between remote objects.
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>
    /// Gets an observable stream of operations received from remote peers.
    /// </summary>
    IObservable<SyncOperation> IncomingOperations { get; }

    /// <summary>
    /// Sends an operation to remote peers.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendOperationAsync(SyncOperation operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to the remote endpoint.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the remote endpoint.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    TransportState State { get; }

    /// <summary>
    /// Occurs when the connection state changes.
    /// </summary>
    event EventHandler<TransportStateChangedEventArgs>? StateChanged;
}

/// <summary>
/// Represents the state of a transport connection.
/// </summary>
public enum TransportState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Disconnecting
}

/// <summary>
/// Event arguments for transport state changes.
/// </summary>
public class TransportStateChangedEventArgs : EventArgs
{
    public TransportStateChangedEventArgs(TransportState oldState, TransportState newState)
    {
        OldState = oldState;
        NewState = newState;
    }

    public TransportState OldState { get; }
    public TransportState NewState { get; }
}
