using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Beutl.Synchronization.Core;
using Microsoft.Extensions.Logging;

namespace Beutl.Synchronization.Transport;

/// <summary>
/// In-memory transport implementation for testing and local synchronization
/// </summary>
public class MemoryTransport : ISyncTransport
{
    private readonly ILogger<MemoryTransport> _logger;
    private readonly Subject<ChangeNotification> _incomingChanges = new();
    private readonly Subject<SyncConnectionStatus> _connectionStatusChanged = new();
    
    private static readonly ConcurrentDictionary<Guid, MemorySession> _sessions = new();
    
    private SyncConnectionStatus _connectionStatus = SyncConnectionStatus.Disconnected;
    private Guid? _currentSessionId;
    private bool _disposed;

    public MemoryTransport(ILogger<MemoryTransport>? logger = null)
    {
        _logger = logger ?? CreateDefaultLogger();
    }

    public IObservable<ChangeNotification> IncomingChanges => _incomingChanges.AsObservable();

    public SyncConnectionStatus ConnectionStatus => _connectionStatus;

    public IObservable<SyncConnectionStatus> ConnectionStatusChanged => _connectionStatusChanged.AsObservable();

    public Task ConnectAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_connectionStatus == SyncConnectionStatus.Connected)
        {
            _logger.LogDebug("Already connected");
            return Task.CompletedTask;
        }

        _logger.LogDebug("Connecting to memory transport for session {SessionId}", sessionId);

        SetConnectionStatus(SyncConnectionStatus.Connecting);
        
        // Simulate async connection
        return Task.Delay(10, cancellationToken).ContinueWith(task =>
        {
            if (task.IsCanceled)
            {
                SetConnectionStatus(SyncConnectionStatus.Failed);
                return;
            }

            SetConnectionStatus(SyncConnectionStatus.Connected);
            _logger.LogInformation("Connected to memory transport");
        }, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_connectionStatus == SyncConnectionStatus.Disconnected)
        {
            _logger.LogDebug("Already disconnected");
            return Task.CompletedTask;
        }

        _logger.LogDebug("Disconnecting from memory transport");

        // Leave current session if any
        if (_currentSessionId.HasValue)
        {
            LeaveSessionAsync(cancellationToken).Wait(cancellationToken);
        }

        SetConnectionStatus(SyncConnectionStatus.Disconnected);
        _logger.LogInformation("Disconnected from memory transport");

        return Task.CompletedTask;
    }

    public Task SendChangeAsync(ChangeNotification change, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_connectionStatus != SyncConnectionStatus.Connected)
        {
            throw new InvalidOperationException("Transport is not connected");
        }

        if (!_currentSessionId.HasValue)
        {
            throw new InvalidOperationException("Not joined to any session");
        }

        _logger.LogTrace("Sending change for object {ObjectId}, property {PropertyName}",
            change.ObjectId, change.PropertyName);

        // Get session and broadcast to other clients
        if (_sessions.TryGetValue(_currentSessionId.Value, out var session))
        {
            session.BroadcastChange(change, this);
        }

        return Task.CompletedTask;
    }

    public Task JoinSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_connectionStatus != SyncConnectionStatus.Connected)
        {
            throw new InvalidOperationException("Transport is not connected");
        }

        _logger.LogDebug("Joining session {SessionId}", sessionId);

        // Leave current session if any
        if (_currentSessionId.HasValue && _currentSessionId != sessionId)
        {
            LeaveSessionAsync(cancellationToken).Wait(cancellationToken);
        }

        // Get or create session
        var session = _sessions.GetOrAdd(sessionId, id => new MemorySession(id, _logger));
        
        // Add this transport to the session
        session.AddClient(this);
        _currentSessionId = sessionId;

        _logger.LogInformation("Joined session {SessionId}", sessionId);

        return Task.CompletedTask;
    }

    public Task LeaveSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_currentSessionId.HasValue)
        {
            _logger.LogDebug("Not joined to any session");
            return Task.CompletedTask;
        }

        _logger.LogDebug("Leaving session {SessionId}", _currentSessionId);

        // Remove this transport from the session
        if (_sessions.TryGetValue(_currentSessionId.Value, out var session))
        {
            session.RemoveClient(this);
            
            // Clean up empty sessions
            if (session.ClientCount == 0)
            {
                _sessions.TryRemove(_currentSessionId.Value, out _);
                session.Dispose();
                _logger.LogDebug("Cleaned up empty session {SessionId}", _currentSessionId);
            }
        }

        var sessionId = _currentSessionId;
        _currentSessionId = null;

        _logger.LogInformation("Left session {SessionId}", sessionId);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogDebug("Disposing MemoryTransport");

        // Disconnect if connected
        if (_connectionStatus != SyncConnectionStatus.Disconnected)
        {
            try
            {
                DisconnectAsync().Wait(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting during disposal");
            }
        }

        _incomingChanges?.Dispose();
        _connectionStatusChanged?.Dispose();

        _disposed = true;
        _logger.LogDebug("MemoryTransport disposed");
    }

    internal void ReceiveChange(ChangeNotification change)
    {
        if (_disposed) return;

        _logger.LogTrace("Received change for object {ObjectId}, property {PropertyName}",
            change.ObjectId, change.PropertyName);

        _incomingChanges.OnNext(change);
    }

    private void SetConnectionStatus(SyncConnectionStatus status)
    {
        if (_connectionStatus == status) return;

        _connectionStatus = status;
        _connectionStatusChanged.OnNext(status);
        
        _logger.LogDebug("Connection status changed to {Status}", status);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MemoryTransport));
        }
    }

    private static ILogger<MemoryTransport> CreateDefaultLogger()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        return loggerFactory.CreateLogger<MemoryTransport>();
    }
}

/// <summary>
/// Represents a memory-based synchronization session
/// </summary>
internal class MemorySession : IDisposable
{
    private readonly Guid _sessionId;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<MemoryTransport, byte> _clients = new();
    private bool _disposed;

    public MemorySession(Guid sessionId, ILogger logger)
    {
        _sessionId = sessionId;
        _logger = logger;
    }

    public int ClientCount => _clients.Count;

    public void AddClient(MemoryTransport client)
    {
        if (_disposed) return;

        if (_clients.TryAdd(client, 0))
        {
            _logger.LogDebug("Added client to session {SessionId}, total clients: {ClientCount}",
                _sessionId, ClientCount);
        }
    }

    public void RemoveClient(MemoryTransport client)
    {
        if (_disposed) return;

        if (_clients.TryRemove(client, out _))
        {
            _logger.LogDebug("Removed client from session {SessionId}, remaining clients: {ClientCount}",
                _sessionId, ClientCount);
        }
    }

    public void BroadcastChange(ChangeNotification change, MemoryTransport sender)
    {
        if (_disposed) return;

        // Send to all clients except the sender
        foreach (var client in _clients.Keys.Where(c => c != sender))
        {
            try
            {
                client.ReceiveChange(change);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error sending change to client in session {SessionId}", _sessionId);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _clients.Clear();
        _disposed = true;
        
        _logger.LogDebug("Session {SessionId} disposed", _sessionId);
    }
}