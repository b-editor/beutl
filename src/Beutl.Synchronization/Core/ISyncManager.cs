using System.Reactive;

namespace Beutl.Synchronization.Core;

/// <summary>
/// Main interface for managing CoreObject synchronization
/// </summary>
public interface ISyncManager : IDisposable
{
    /// <summary>
    /// Observable stream of changes received from remote sources
    /// </summary>
    IObservable<ChangeNotification> RemoteChanges { get; }

    /// <summary>
    /// Observable stream of changes originating from local sources
    /// </summary>
    IObservable<ChangeNotification> LocalChanges { get; }

    /// <summary>
    /// Current synchronization session ID
    /// </summary>
    Guid? SessionId { get; }

    /// <summary>
    /// Whether synchronization is currently active
    /// </summary>
    bool IsSyncEnabled { get; }

    /// <summary>
    /// Connection status
    /// </summary>
    SyncConnectionStatus ConnectionStatus { get; }

    /// <summary>
    /// Start synchronization for a specific session
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartSyncAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop synchronization
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StopSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a change notification to remote clients
    /// </summary>
    /// <param name="change">Change notification to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendChangeAsync(ChangeNotification change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Register a CoreObject for synchronization
    /// </summary>
    /// <param name="obj">CoreObject to register</param>
    void RegisterObject(CoreObject obj);

    /// <summary>
    /// Unregister a CoreObject from synchronization
    /// </summary>
    /// <param name="obj">CoreObject to unregister</param>
    void UnregisterObject(CoreObject obj);

    /// <summary>
    /// Check if an object is registered for synchronization
    /// </summary>
    /// <param name="obj">CoreObject to check</param>
    bool IsObjectRegistered(CoreObject obj);

    /// <summary>
    /// Observable for connection status changes
    /// </summary>
    IObservable<SyncConnectionStatus> ConnectionStatusChanged { get; }
}

/// <summary>
/// Synchronization connection status
/// </summary>
public enum SyncConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}