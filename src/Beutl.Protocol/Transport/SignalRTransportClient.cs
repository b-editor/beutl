using System.Reactive.Subjects;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Protocol.Transport;

/// <summary>
/// SignalR-based transport client for synchronizing operations.
/// </summary>
public class SignalRTransportClient : ITransport
{
    private readonly HubConnection _connection;
    private readonly Subject<OperationBase> _incomingOperations;
    private readonly SemaphoreSlim _connectionLock;
    private TransportState _state;
    private bool _disposed;

    public SignalRTransportClient(string hubUrl, Action<IHubConnectionBuilder>? configureConnection = null)
    {
        _incomingOperations = new Subject<OperationBase>();
        _connectionLock = new SemaphoreSlim(1, 1);
        _state = TransportState.Disconnected;

        var builder = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new CustomRetryPolicy())
            .AddJsonProtocol();

        configureConnection?.Invoke(builder);

        _connection = builder.Build();

        SetupConnectionHandlers();
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

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection.State == HubConnectionState.Connected)
            {
                return;
            }

            State = TransportState.Connecting;
            await _connection.StartAsync(cancellationToken);
            State = TransportState.Connected;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection.State == HubConnectionState.Disconnected)
            {
                return;
            }

            State = TransportState.Disconnecting;
            await _connection.StopAsync(cancellationToken);
            State = TransportState.Disconnected;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task SendOperationAsync(OperationBase operation, CancellationToken cancellationToken = default)
    {
        if (_connection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Cannot send operation when not connected");
        }

        var errorHandler = new TransportErrorHandler();
        await errorHandler.ExecuteWithRetryAsync(async () =>
        {
            var json = OperationSerializer.Serialize(operation);
            await _connection.InvokeAsync("BroadcastOperationAsync", json, cancellationToken);
        }, cancellationToken);
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
        _connectionLock.Dispose();

        _ = _connection.DisposeAsync().AsTask();
        
        GC.SuppressFinalize(this);
    }

    private void SetupConnectionHandlers()
    {
        _connection.On<string>("ReceiveOperationAsync", (json) =>
        {
            try
            {
                var operation = OperationSerializer.Deserialize(json);
                _incomingOperations.OnNext(operation);
            }
            catch (Exception ex)
            {
                // Log error but don't propagate to avoid breaking the connection
                Console.WriteLine($"Failed to deserialize operation: {ex.Message}");
            }
        });

        _connection.Closed += async (error) =>
        {
            State = TransportState.Disconnected;
            if (error != null)
            {
                Console.WriteLine($"Connection closed with error: {error.Message}");
            }
            await Task.CompletedTask;
        };

        _connection.Reconnecting += async (error) =>
        {
            State = TransportState.Reconnecting;
            if (error != null)
            {
                Console.WriteLine($"Connection reconnecting due to error: {error.Message}");
            }
            await Task.CompletedTask;
        };

        _connection.Reconnected += async (connectionId) =>
        {
            State = TransportState.Connected;
            Console.WriteLine($"Connection reconnected with ID: {connectionId}");
            await Task.CompletedTask;
        };
    }

    private class CustomRetryPolicy : IRetryPolicy
    {
        private readonly TimeSpan[] _retryDelays = new[]
        {
            TimeSpan.FromSeconds(0),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        };

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            if (retryContext.PreviousRetryCount >= _retryDelays.Length)
            {
                return null; // Stop retrying
            }

            return _retryDelays[retryContext.PreviousRetryCount];
        }
    }
}
