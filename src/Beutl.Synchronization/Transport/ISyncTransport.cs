using Beutl.Synchronization.Core;

namespace Beutl.Synchronization.Transport;

/// <summary>
/// Abstract interface for synchronization transport layer
/// </summary>
public interface ISyncTransport : IDisposable
{
    /// <summary>
    /// Observable stream of incoming change notifications
    /// </summary>
    IObservable<ChangeNotification> IncomingChanges { get; }

    /// <summary>
    /// Connection status
    /// </summary>
    SyncConnectionStatus ConnectionStatus { get; }

    /// <summary>
    /// Observable for connection status changes
    /// </summary>
    IObservable<SyncConnectionStatus> ConnectionStatusChanged { get; }

    /// <summary>
    /// Connect to the synchronization server
    /// </summary>
    /// <param name="sessionId">Session to join</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ConnectAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the synchronization server
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a change notification to other clients
    /// </summary>
    /// <param name="change">Change notification</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendChangeAsync(ChangeNotification change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Join a specific project/session for synchronization
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task JoinSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Leave the current session
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LeaveSessionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for transport layer
/// </summary>
public class SyncTransportConfig
{
    /// <summary>
    /// Server URL for synchronization
    /// </summary>
    public required string ServerUrl { get; init; }

    /// <summary>
    /// Connection timeout in milliseconds
    /// </summary>
    public int ConnectionTimeoutMs { get; init; } = 30000;

    /// <summary>
    /// Automatic reconnection attempts
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 5;

    /// <summary>
    /// Delay between reconnection attempts in milliseconds
    /// </summary>
    public int ReconnectDelayMs { get; init; } = 1000;

    /// <summary>
    /// Authentication token if required
    /// </summary>
    public string? AuthToken { get; init; }

    /// <summary>
    /// Additional headers for transport
    /// </summary>
    public Dictionary<string, string> Headers { get; init; } = new();
}