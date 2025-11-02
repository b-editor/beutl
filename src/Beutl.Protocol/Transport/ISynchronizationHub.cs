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
}
