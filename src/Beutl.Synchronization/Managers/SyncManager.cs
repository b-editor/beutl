using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Beutl.Synchronization.Core;
using Beutl.Synchronization.Transport;
using Microsoft.Extensions.Logging;

namespace Beutl.Synchronization.Managers;

/// <summary>
/// Default implementation of ISyncManager
/// </summary>
public class SyncManager : ISyncManager
{
    private readonly ISyncTransport _transport;
    private readonly ILogger<SyncManager> _logger;
    private readonly Subject<ChangeNotification> _localChanges = new();
    private readonly Subject<SyncConnectionStatus> _connectionStatusChanged = new();
    private readonly ConcurrentDictionary<CoreObject, byte> _registeredObjects = new();
    
    private bool _disposed;
    private IDisposable? _transportSubscription;

    public SyncManager(ISyncTransport transport, ILogger<SyncManager>? logger = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? CreateDefaultLogger();

        // Subscribe to transport changes
        _transportSubscription = _transport.ConnectionStatusChanged
            .Subscribe(status => _connectionStatusChanged.OnNext(status));
    }

    public IObservable<ChangeNotification> RemoteChanges => _transport.IncomingChanges;

    public IObservable<ChangeNotification> LocalChanges => _localChanges.AsObservable();

    public Guid? SessionId { get; private set; }

    public bool IsSyncEnabled => SessionId.HasValue && 
                                ConnectionStatus == SyncConnectionStatus.Connected;

    public SyncConnectionStatus ConnectionStatus => _transport.ConnectionStatus;

    public IObservable<SyncConnectionStatus> ConnectionStatusChanged => _connectionStatusChanged.AsObservable();

    public async Task StartSyncAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (SessionId == sessionId && IsSyncEnabled)
        {
            _logger.LogDebug("Already synchronized to session {SessionId}", sessionId);
            return;
        }

        try
        {
            _logger.LogInformation("Starting synchronization for session {SessionId}", sessionId);

            // Connect to transport
            await _transport.ConnectAsync(sessionId, cancellationToken);
            
            // Join the session
            await _transport.JoinSessionAsync(sessionId, cancellationToken);

            SessionId = sessionId;

            _logger.LogInformation("Successfully started synchronization for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start synchronization for session {SessionId}", sessionId);
            SessionId = null;
            throw;
        }
    }

    public async Task StopSyncAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!SessionId.HasValue)
        {
            _logger.LogDebug("Synchronization is not active");
            return;
        }

        try
        {
            _logger.LogInformation("Stopping synchronization for session {SessionId}", SessionId);

            // Leave the session
            await _transport.LeaveSessionAsync(cancellationToken);
            
            // Disconnect from transport
            await _transport.DisconnectAsync(cancellationToken);

            SessionId = null;

            _logger.LogInformation("Successfully stopped synchronization");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping synchronization");
            throw;
        }
    }

    public async Task SendChangeAsync(ChangeNotification change, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!IsSyncEnabled)
        {
            _logger.LogWarning("Attempted to send change when synchronization is not enabled");
            return;
        }

        try
        {
            // Ensure session ID is set
            var changeWithSession = change with 
            { 
                SessionId = SessionId
            };

            // Send through transport
            await _transport.SendChangeAsync(changeWithSession, cancellationToken);

            // Notify local observers
            _localChanges.OnNext(changeWithSession);

            _logger.LogTrace("Sent change for object {ObjectId}, property {PropertyName}",
                change.ObjectId, change.PropertyName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send change for object {ObjectId}, property {PropertyName}",
                change.ObjectId, change.PropertyName);
            throw;
        }
    }

    public void RegisterObject(CoreObject obj)
    {
        ThrowIfDisposed();

        if (obj == null) throw new ArgumentNullException(nameof(obj));

        if (_registeredObjects.TryAdd(obj, 0))
        {
            _logger.LogDebug("Registered object {ObjectId} of type {ObjectType}",
                obj.Id, obj.GetType().Name);
        }
    }

    public void UnregisterObject(CoreObject obj)
    {
        ThrowIfDisposed();

        if (obj == null) throw new ArgumentNullException(nameof(obj));

        if (_registeredObjects.TryRemove(obj, out _))
        {
            _logger.LogDebug("Unregistered object {ObjectId} of type {ObjectType}",
                obj.Id, obj.GetType().Name);
        }
    }

    public bool IsObjectRegistered(CoreObject obj)
    {
        ThrowIfDisposed();

        if (obj == null) return false;
        
        return _registeredObjects.ContainsKey(obj);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogDebug("Disposing SyncManager");

        // Stop synchronization
        try
        {
            StopSyncAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping synchronization during disposal");
        }

        // Dispose subscriptions
        _transportSubscription?.Dispose();
        _localChanges?.Dispose();
        _connectionStatusChanged?.Dispose();

        // Dispose transport
        _transport?.Dispose();

        // Clear registered objects
        _registeredObjects.Clear();

        _disposed = true;
        _logger.LogDebug("SyncManager disposed");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SyncManager));
        }
    }

    private static ILogger<SyncManager> CreateDefaultLogger()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        return loggerFactory.CreateLogger<SyncManager>();
    }
}

