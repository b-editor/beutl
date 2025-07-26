using System.Reactive.Linq;
using System.Reactive.Subjects;
using Beutl.Synchronization.Core;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Beutl.Synchronization.Transport;

/// <summary>
/// SignalR-based transport implementation for real-time synchronization
/// </summary>
public class SignalRTransport : ISyncTransport
{
    private readonly SyncTransportConfig _config;
    private readonly ILogger<SignalRTransport> _logger;
    private readonly Subject<ChangeNotification> _incomingChanges = new();
    private readonly Subject<SyncConnectionStatus> _connectionStatusChanged = new();
    
    private HubConnection? _connection;
    private SyncConnectionStatus _connectionStatus = SyncConnectionStatus.Disconnected;
    private Guid? _currentSessionId;
    private bool _disposed;
    private int _reconnectAttempts;

    public SignalRTransport(SyncTransportConfig config, ILogger<SignalRTransport>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? CreateDefaultLogger();
    }

    public IObservable<ChangeNotification> IncomingChanges => _incomingChanges.AsObservable();

    public SyncConnectionStatus ConnectionStatus => _connectionStatus;

    public IObservable<SyncConnectionStatus> ConnectionStatusChanged => _connectionStatusChanged.AsObservable();

    public async Task ConnectAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_connectionStatus == SyncConnectionStatus.Connected)
        {
            _logger.LogDebug("Already connected to SignalR hub");
            return;
        }

        try
        {
            _logger.LogInformation("Connecting to SignalR hub at {ServerUrl}", _config.ServerUrl);
            SetConnectionStatus(SyncConnectionStatus.Connecting);

            // Build the connection
            var connectionBuilder = new HubConnectionBuilder()
                .WithUrl(_config.ServerUrl, options =>
                {
                    if (!string.IsNullOrEmpty(_config.AuthToken))
                    {
                        options.AccessTokenProvider = () => Task.FromResult(_config.AuthToken);
                    }

                    foreach (var header in _config.Headers)
                    {
                        options.Headers.Add(header.Key, header.Value);
                    }
                })
                .WithAutomaticReconnect(new RetryPolicy(_config.MaxReconnectAttempts, _config.ReconnectDelayMs))
                .ConfigureLogging(logging =>
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                    }
                });

            _connection = connectionBuilder.Build();

            // Setup event handlers
            SetupConnectionEventHandlers();
            SetupSignalRHandlers();

            // Start the connection with timeout
            using var timeoutCts = new CancellationTokenSource(_config.ConnectionTimeoutMs);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await _connection.StartAsync(combinedCts.Token);

            SetConnectionStatus(SyncConnectionStatus.Connected);
            _reconnectAttempts = 0;

            _logger.LogInformation("Successfully connected to SignalR hub");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Connection attempt was cancelled");
            SetConnectionStatus(SyncConnectionStatus.Disconnected);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to SignalR hub");
            SetConnectionStatus(SyncConnectionStatus.Failed);
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_connection == null || _connectionStatus == SyncConnectionStatus.Disconnected)
        {
            _logger.LogDebug("Already disconnected from SignalR hub");
            return;
        }

        try
        {
            _logger.LogInformation("Disconnecting from SignalR hub");

            // Leave current session if any
            if (_currentSessionId.HasValue)
            {
                await LeaveSessionAsync(cancellationToken);
            }

            await _connection.StopAsync(cancellationToken);
            await _connection.DisposeAsync();
            _connection = null;

            SetConnectionStatus(SyncConnectionStatus.Disconnected);
            _logger.LogInformation("Successfully disconnected from SignalR hub");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from SignalR hub");
            throw;
        }
    }

    public async Task SendChangeAsync(ChangeNotification change, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_connection == null || _connectionStatus != SyncConnectionStatus.Connected)
        {
            throw new InvalidOperationException("SignalR connection is not active");
        }

        if (!_currentSessionId.HasValue)
        {
            throw new InvalidOperationException("Not joined to any session");
        }

        try
        {
            _logger.LogTrace("Sending change via SignalR: {ObjectId}.{PropertyName}",
                change.ObjectId, change.PropertyName);

            await _connection.InvokeAsync("SendChange", _currentSessionId.ToString(), change, cancellationToken);

            _logger.LogTrace("Successfully sent change via SignalR");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send change via SignalR");
            throw;
        }
    }

    public async Task JoinSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_connection == null || _connectionStatus != SyncConnectionStatus.Connected)
        {
            throw new InvalidOperationException("SignalR connection is not active");
        }

        try
        {
            _logger.LogInformation("Joining SignalR session {SessionId}", sessionId);

            // Leave current session if different
            if (_currentSessionId.HasValue && _currentSessionId != sessionId)
            {
                await LeaveSessionAsync(cancellationToken);
            }

            await _connection.InvokeAsync("JoinProject", sessionId.ToString(), cancellationToken);
            _currentSessionId = sessionId;

            _logger.LogInformation("Successfully joined SignalR session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join SignalR session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task LeaveSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_currentSessionId.HasValue)
        {
            _logger.LogDebug("Not joined to any session");
            return;
        }

        if (_connection == null || _connectionStatus != SyncConnectionStatus.Connected)
        {
            _logger.LogWarning("Cannot leave session - SignalR connection is not active");
            _currentSessionId = null;
            return;
        }

        try
        {
            _logger.LogInformation("Leaving SignalR session {SessionId}", _currentSessionId);

            await _connection.InvokeAsync("LeaveProject", _currentSessionId.ToString(), cancellationToken);
            
            var sessionId = _currentSessionId;
            _currentSessionId = null;

            _logger.LogInformation("Successfully left SignalR session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave SignalR session {SessionId}", _currentSessionId);
            _currentSessionId = null;
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogDebug("Disposing SignalRTransport");

        try
        {
            DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disconnecting during disposal");
        }

        _incomingChanges?.Dispose();
        _connectionStatusChanged?.Dispose();

        _disposed = true;
        _logger.LogDebug("SignalRTransport disposed");
    }

    private void SetupConnectionEventHandlers()
    {
        if (_connection == null) return;

        _connection.Closed += async (error) =>
        {
            if (error != null)
            {
                _logger.LogError(error, "SignalR connection closed with error");
                SetConnectionStatus(SyncConnectionStatus.Failed);
            }
            else
            {
                _logger.LogInformation("SignalR connection closed");
                SetConnectionStatus(SyncConnectionStatus.Disconnected);
            }

            _currentSessionId = null;
        };

        _connection.Reconnecting += (error) =>
        {
            _logger.LogWarning(error, "SignalR connection lost, attempting to reconnect...");
            SetConnectionStatus(SyncConnectionStatus.Reconnecting);
            return Task.CompletedTask;
        };

        _connection.Reconnected += (connectionId) =>
        {
            _logger.LogInformation("SignalR connection restored. ConnectionId: {ConnectionId}", connectionId);
            SetConnectionStatus(SyncConnectionStatus.Connected);
            _reconnectAttempts = 0;

            // Rejoin session if we were in one
            if (_currentSessionId.HasValue)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await JoinSessionAsync(_currentSessionId.Value);
                        _logger.LogInformation("Rejoined session {SessionId} after reconnection", _currentSessionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to rejoin session {SessionId} after reconnection", _currentSessionId);
                    }
                });
            }

            return Task.CompletedTask;
        };
    }

    private void SetupSignalRHandlers()
    {
        if (_connection == null) return;

        _connection.On<ChangeNotification>("ReceiveChange", change =>
        {
            try
            {
                _logger.LogTrace("Received change via SignalR: {ObjectId}.{PropertyName}",
                    change.ObjectId, change.PropertyName);

                _incomingChanges.OnNext(change);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing received change");
            }
        });

        _connection.On<string>("SessionJoined", sessionId =>
        {
            _logger.LogDebug("Confirmed session join: {SessionId}", sessionId);
        });

        _connection.On<string>("SessionLeft", sessionId =>
        {
            _logger.LogDebug("Confirmed session leave: {SessionId}", sessionId);
        });

        _connection.On<string, int>("SessionMemberCountChanged", (sessionId, memberCount) =>
        {
            _logger.LogDebug("Session {SessionId} member count changed to {MemberCount}", sessionId, memberCount);
        });
    }

    private void SetConnectionStatus(SyncConnectionStatus status)
    {
        if (_connectionStatus == status) return;

        var previousStatus = _connectionStatus;
        _connectionStatus = status;
        _connectionStatusChanged.OnNext(status);

        _logger.LogDebug("SignalR connection status changed from {PreviousStatus} to {NewStatus}",
            previousStatus, status);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SignalRTransport));
        }
    }

    private static ILogger<SignalRTransport> CreateDefaultLogger()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        return loggerFactory.CreateLogger<SignalRTransport>();
    }
}

/// <summary>
/// Custom retry policy for SignalR reconnection
/// </summary>
public class RetryPolicy : IRetryPolicy
{
    private readonly int _maxAttempts;
    private readonly int _baseDelayMs;

    public RetryPolicy(int maxAttempts, int baseDelayMs)
    {
        _maxAttempts = maxAttempts;
        _baseDelayMs = baseDelayMs;
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        if (retryContext.PreviousRetryCount >= _maxAttempts)
        {
            return null; // Stop retrying
        }

        // Exponential backoff with jitter
        var delay = _baseDelayMs * Math.Pow(2, retryContext.PreviousRetryCount);
        var jitter = Random.Shared.NextDouble() * 0.1 * delay; // Add up to 10% jitter
        
        return TimeSpan.FromMilliseconds(delay + jitter);
    }
}