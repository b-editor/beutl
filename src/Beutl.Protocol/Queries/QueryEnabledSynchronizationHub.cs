using Microsoft.AspNetCore.SignalR;

namespace Beutl.Protocol.Queries;

public class QueryEnabledSynchronizationHub : Hub, IQueryHub
{
    private static QueryHubServer<QueryEnabledSynchronizationHub>? _queryServer;
    private static readonly object _lock = new();

    public static void Initialize(ICoreObject root, IHubContext<QueryEnabledSynchronizationHub> hubContext)
    {
        lock (_lock)
        {
            _queryServer = new QueryHubServer<QueryEnabledSynchronizationHub>(root, hubContext);
        }
    }

    protected static QueryHubServer<QueryEnabledSynchronizationHub>? QueryServer => _queryServer;

    public async Task<string> ExecuteQueryAsync(string queryJson, string? targetId = null)
    {
        if (_queryServer == null)
        {
            throw new InvalidOperationException("Query server not initialized. Call Initialize() first.");
        }

        return await _queryServer.ExecuteQueryAsync(queryJson, targetId, Context.ConnectionId);
    }

    public async Task<string> ExecuteMutationAsync(string mutationJson, string? returnSchemaJson = null)
    {
        if (_queryServer == null)
        {
            throw new InvalidOperationException("Query server not initialized. Call Initialize() first.");
        }

        return await _queryServer.ExecuteMutationAsync(mutationJson, returnSchemaJson, Context.ConnectionId);
    }

    public async Task<bool> SubscribeAsync(string subscriptionId, string queryJson, string? targetId = null)
    {
        if (_queryServer == null)
        {
            throw new InvalidOperationException("Query server not initialized. Call Initialize() first.");
        }

        return await _queryServer.SubscribeAsync(subscriptionId, queryJson, targetId, Context.ConnectionId);
    }

    public async Task<bool> UnsubscribeAsync(string subscriptionId)
    {
        if (_queryServer == null)
        {
            throw new InvalidOperationException("Query server not initialized. Call Initialize() first.");
        }

        return await _queryServer.UnsubscribeAsync(subscriptionId, Context.ConnectionId);
    }

    public async Task<string> GetSubscriptionStateAsync(string subscriptionId)
    {
        if (_queryServer == null)
        {
            throw new InvalidOperationException("Query server not initialized. Call Initialize() first.");
        }

        return await _queryServer.GetSubscriptionStateAsync(subscriptionId, Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _queryServer?.OnClientDisconnected(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
