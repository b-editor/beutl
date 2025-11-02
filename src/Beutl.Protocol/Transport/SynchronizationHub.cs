using Microsoft.AspNetCore.SignalR;

namespace Beutl.Protocol.Transport;

/// <summary>
/// SignalR hub for broadcasting operations between clients.
/// This hub is stateless and acts as a message broker for peer-to-peer communication.
/// </summary>
public class SynchronizationHub : Hub<ISynchronizationClient>
{
    /// <summary>
    /// Broadcasts an operation to all connected clients except the sender.
    /// </summary>
    /// <param name="operation">The serialized operation.</param>
    public async Task BroadcastOperationAsync(string operation)
    {
        // Broadcast to all clients except the sender
        await Clients.Others.ReceiveOperationAsync(operation);
    }

    /// <summary>
    /// Sends an operation to a specific group.
    /// </summary>
    /// <param name="groupName">The name of the group.</param>
    /// <param name="operation">The serialized operation.</param>
    public async Task SendToGroupAsync(string groupName, string operation)
    {
        await Clients.Group(groupName).ReceiveOperationAsync(operation);
    }

    /// <summary>
    /// Adds the current connection to a group.
    /// </summary>
    /// <param name="groupName">The name of the group to join.</param>
    public async Task JoinGroupAsync(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Removes the current connection from a group.
    /// </summary>
    /// <param name="groupName">The name of the group to leave.</param>
    public async Task LeaveGroupAsync(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.OnConnectionStateChangedAsync("Connected");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.Caller.OnConnectionStateChangedAsync("Disconnected");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Requests the initial state from any available peer client.
    /// The hub broadcasts the request to all other clients.
    /// </summary>
    public async Task RequestInitialStateFromPeerAsync()
    {
        // Broadcast request to all other clients
        // The first client to respond will send their state
        await Clients.Others.RequestStateFromPeerAsync(Context.ConnectionId);
    }

    /// <summary>
    /// Sends the current state to a specific peer client.
    /// </summary>
    /// <param name="targetConnectionId">The connection ID of the requesting client.</param>
    /// <param name="state">The serialized state.</param>
    public async Task SendStateToPeerAsync(string targetConnectionId, string state)
    {
        // Send state directly to the requesting client
        await Clients.Client(targetConnectionId).ReceiveStateFromPeerAsync(state);
    }
}
