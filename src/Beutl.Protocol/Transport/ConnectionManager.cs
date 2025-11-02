using System.Collections.Concurrent;

namespace Beutl.Protocol.Transport;

/// <summary>
/// Manages multiple transport connections and their lifecycle.
/// </summary>
public class ConnectionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ITransport> _connections;
    private readonly ConcurrentDictionary<string, RemoteSynchronizer> _synchronizers;
    private bool _disposed;

    public ConnectionManager()
    {
        _connections = new ConcurrentDictionary<string, ITransport>();
        _synchronizers = new ConcurrentDictionary<string, RemoteSynchronizer>();
    }

    /// <summary>
    /// Adds a new connection with the specified ID.
    /// </summary>
    /// <param name="connectionId">The unique identifier for the connection.</param>
    /// <param name="transport">The transport to add.</param>
    /// <returns>True if the connection was added, false if it already exists.</returns>
    public bool AddConnection(string connectionId, ITransport transport)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            throw new ArgumentException("Connection ID cannot be null or whitespace", nameof(connectionId));
        }

        if (transport == null)
        {
            throw new ArgumentNullException(nameof(transport));
        }

        if (_connections.TryAdd(connectionId, transport))
        {
            transport.StateChanged += (sender, args) =>
                OnConnectionStateChanged(connectionId, args);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes and disposes a connection with the specified ID.
    /// </summary>
    /// <param name="connectionId">The unique identifier for the connection.</param>
    /// <returns>True if the connection was removed, false if it doesn't exist.</returns>
    public bool RemoveConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var transport))
        {
            transport.Dispose();

            if (_synchronizers.TryRemove(connectionId, out var synchronizer))
            {
                synchronizer.Dispose();
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a connection by its ID.
    /// </summary>
    /// <param name="connectionId">The unique identifier for the connection.</param>
    /// <returns>The transport if found, null otherwise.</returns>
    public ITransport? GetConnection(string connectionId)
    {
        _connections.TryGetValue(connectionId, out var transport);
        return transport;
    }

    /// <summary>
    /// Creates and adds a remote synchronizer for the specified connection.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="localSynchronizer">The local synchronizer.</param>
    /// <param name="executor">The operation executor.</param>
    /// <returns>True if the synchronizer was added, false if it already exists.</returns>
    public bool AddSynchronizer(
        string connectionId,
        ISynchronizer localSynchronizer,
        OperationExecutor executor)
    {
        if (!_connections.TryGetValue(connectionId, out var transport))
        {
            throw new InvalidOperationException($"Connection '{connectionId}' not found");
        }

        var synchronizer = new RemoteSynchronizer(localSynchronizer, transport, executor);
        return _synchronizers.TryAdd(connectionId, synchronizer);
    }

    /// <summary>
    /// Gets all active connection IDs.
    /// </summary>
    public IEnumerable<string> GetConnectionIds()
    {
        return _connections.Keys;
    }

    /// <summary>
    /// Gets the number of active connections.
    /// </summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>
    /// Occurs when a connection's state changes.
    /// </summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    private void OnConnectionStateChanged(string connectionId, TransportStateChangedEventArgs args)
    {
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(
            connectionId, args.OldState, args.NewState));

        // Auto-cleanup on disconnection
        if (args.NewState == TransportState.Disconnected)
        {
            RemoveConnection(connectionId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var synchronizer in _synchronizers.Values)
        {
            synchronizer.Dispose();
        }
        _synchronizers.Clear();

        foreach (var transport in _connections.Values)
        {
            transport.Dispose();
        }
        _connections.Clear();
    }
}

/// <summary>
/// Event arguments for connection state changes.
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionStateChangedEventArgs(
        string connectionId,
        TransportState oldState,
        TransportState newState)
    {
        ConnectionId = connectionId;
        OldState = oldState;
        NewState = newState;
    }

    public string ConnectionId { get; }
    public TransportState OldState { get; }
    public TransportState NewState { get; }
}
