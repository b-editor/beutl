namespace Beutl.Protocol.Transport;

/// <summary>
/// Defines the contract for a SignalR hub that handles operation synchronization.
/// </summary>
public interface ISynchronizationHub
{
    /// <summary>
    /// Sends an operation to all connected clients except the sender.
    /// </summary>
    /// <param name="operation">The serialized operation.</param>
    Task BroadcastOperationAsync(string operation);

    /// <summary>
    /// Sends an operation to a specific group.
    /// </summary>
    /// <param name="groupName">The name of the group.</param>
    /// <param name="operation">The serialized operation.</param>
    Task SendToGroupAsync(string groupName, string operation);

    /// <summary>
    /// Requests the initial state from any available peer client (peer-to-peer).
    /// The server broadcasts the request to all clients, and the first client to respond provides the state.
    /// </summary>
    Task RequestInitialStateFromPeerAsync();

    /// <summary>
    /// Sends the current state to a specific peer client.
    /// </summary>
    /// <param name="targetConnectionId">The connection ID of the requesting client.</param>
    /// <param name="state">The serialized state.</param>
    Task SendStateToPeerAsync(string targetConnectionId, string state);
}

/// <summary>
/// Defines the contract for client-side methods that can be called from the server.
/// </summary>
public interface ISynchronizationClient
{
    /// <summary>
    /// Called when an operation is received from the server.
    /// </summary>
    /// <param name="operation">The serialized operation.</param>
    Task ReceiveOperationAsync(string operation);

    /// <summary>
    /// Called when the connection state changes.
    /// </summary>
    /// <param name="state">The new connection state.</param>
    Task OnConnectionStateChangedAsync(string state);

    /// <summary>
    /// Called when another client requests the current state (peer-to-peer state transfer).
    /// </summary>
    /// <param name="requestingConnectionId">The connection ID of the requesting client.</param>
    Task RequestStateFromPeerAsync(string requestingConnectionId);

    /// <summary>
    /// Called when a peer sends its state in response to a request.
    /// </summary>
    /// <param name="state">The serialized state from the peer.</param>
    Task ReceiveStateFromPeerAsync(string state);
}
