using System.Reactive.Subjects;
using Microsoft.AspNetCore.SignalR;

namespace Beutl.Protocol.Transport;

/// <summary>
/// Server-side SignalR transport for broadcasting operations to connected clients.
/// </summary>
public class SignalRTransportServer : ITransport
{
    private readonly IHubContext<SynchronizationHub, ISynchronizationClient> _hubContext;
    private readonly Subject<OperationBase> _incomingOperations;
    private TransportState _state;
    private bool _disposed;

    public SignalRTransportServer(IHubContext<SynchronizationHub, ISynchronizationClient> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _incomingOperations = new Subject<OperationBase>();
        _state = TransportState.Connected; // Server is always "connected"
    }

    public IObservable<OperationBase> IncomingOperations => _incomingOperations;

    public TransportState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                var oldState = _state;
                _state = value;
                StateChanged?.Invoke(this, new TransportStateChangedEventArgs(oldState, value));
            }
        }
    }

    public event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // Server doesn't need to connect
        State = TransportState.Connected;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        // Server doesn't disconnect, but we can mark as disconnected
        State = TransportState.Disconnected;
        return Task.CompletedTask;
    }

    public async Task SendOperationAsync(OperationBase operation, CancellationToken cancellationToken = default)
    {
        if (_state != TransportState.Connected)
        {
            throw new InvalidOperationException("Cannot send operation when not connected");
        }

        var json = OperationSerializer.Serialize(operation);
        await _hubContext.Clients.All.ReceiveOperationAsync(json);
    }

    public async Task SendOperationToGroupAsync(string groupName, OperationBase operation, CancellationToken cancellationToken = default)
    {
        if (_state != TransportState.Connected)
        {
            throw new InvalidOperationException("Cannot send operation when not connected");
        }

        var json = OperationSerializer.Serialize(operation);
        await _hubContext.Clients.Group(groupName).ReceiveOperationAsync(json);
    }

    /// <summary>
    /// Called by the hub when an operation is received from a client.
    /// This allows the server to process incoming operations.
    /// </summary>
    public void ReceiveOperation(OperationBase operation)
    {
        _incomingOperations.OnNext(operation);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _incomingOperations.OnCompleted();
        _incomingOperations.Dispose();

        State = TransportState.Disconnected;
    }
}
