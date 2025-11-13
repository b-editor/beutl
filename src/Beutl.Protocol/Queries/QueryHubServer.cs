using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

namespace Beutl.Protocol.Queries;

public class QueryHubServer<THub> where THub : Hub
{
    private readonly ICoreObject _root;
    private readonly QueryExecutor _queryExecutor;
    private readonly MutationExecutor _mutationExecutor;
    private readonly SubscriptionByIdManager _subscriptionManager;
    private readonly IHubContext<THub> _hubContext;
    private readonly Dictionary<string, string> _subscriptionToConnectionId = new();

    public QueryHubServer(ICoreObject root, IHubContext<THub> hubContext)
    {
        _root = root;
        _queryExecutor = new QueryExecutor();
        _mutationExecutor = new MutationExecutor();
        _subscriptionManager = new SubscriptionByIdManager(root);
        _hubContext = hubContext;
    }

    public async Task<string> ExecuteQueryAsync(string queryJson, string? targetIdStr, string connectionId)
    {
        try
        {
            var queryDict = JsonSerializer.Deserialize<Dictionary<string, object>>(queryJson);
            if (queryDict == null)
            {
                return CreateErrorResponse("Invalid query JSON.");
            }

            QuerySchema schema = QuerySchema.FromJson(queryDict);

            QueryResult result;
            if (!string.IsNullOrEmpty(targetIdStr) && Guid.TryParse(targetIdStr, out Guid targetId))
            {
                var queryResult = _queryExecutor.ExecuteById(_root, targetId, schema);
                result = queryResult ?? new QueryResult(null, new Dictionary<string, object?> { ["error"] = "Object not found" });
            }
            else
            {
                result = _queryExecutor.Execute(_root, schema);
            }

            return result.ToJson();
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Query execution failed: {ex.Message}");
        }
    }

    public async Task<string> ExecuteMutationAsync(string mutationJson, string? returnSchemaJson, string connectionId)
    {
        try
        {
            var mutationDict = JsonSerializer.Deserialize<Dictionary<string, object>>(mutationJson);
            if (mutationDict == null)
            {
                return CreateErrorResponse("Invalid mutation JSON.");
            }

            Mutation? mutation = ParseMutation(mutationDict);
            if (mutation == null)
            {
                return CreateErrorResponse("Failed to parse mutation.");
            }

            QuerySchema? returnSchema = null;
            if (!string.IsNullOrEmpty(returnSchemaJson))
            {
                var schemaDict = JsonSerializer.Deserialize<Dictionary<string, object>>(returnSchemaJson);
                if (schemaDict != null)
                {
                    returnSchema = QuerySchema.FromJson(schemaDict);
                }
            }

            MutationResult result = _mutationExecutor.Execute(_root, mutation, returnSchema);
            return result.ToJson();
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Mutation execution failed: {ex.Message}");
        }
    }

    public async Task<bool> SubscribeAsync(string subscriptionId, string queryJson, string? targetIdStr, string connectionId)
    {
        try
        {
            var queryDict = JsonSerializer.Deserialize<Dictionary<string, object>>(queryJson);
            if (queryDict == null)
            {
                await SendErrorToClient(connectionId, "Invalid query JSON.");
                return false;
            }

            QuerySchema schema = QuerySchema.FromJson(queryDict);

            QuerySubscription? subscription;
            if (!string.IsNullOrEmpty(targetIdStr) && Guid.TryParse(targetIdStr, out Guid targetId))
            {
                subscription = _subscriptionManager.SubscribeById(subscriptionId, targetId, schema);
            }
            else
            {
                subscription = _subscriptionManager.SubscribeById(subscriptionId, _root.Id, schema);
            }

            if (subscription == null)
            {
                await SendErrorToClient(connectionId, "Failed to create subscription.");
                return false;
            }

            // Store connection ID for this subscription
            _subscriptionToConnectionId[subscriptionId] = connectionId;

            // Subscribe to updates and forward them to the client
            subscription.Updates.Subscribe(update =>
            {
                _ = SendUpdateToClient(connectionId, subscriptionId, update);
            });

            return true;
        }
        catch (Exception ex)
        {
            await SendErrorToClient(connectionId, $"Subscription failed: {ex.Message}");
            return false;
        }
    }

    public Task<bool> UnsubscribeAsync(string subscriptionId, string connectionId)
    {
        _subscriptionToConnectionId.Remove(subscriptionId);
        bool success = _subscriptionManager.Unsubscribe(subscriptionId);
        return Task.FromResult(success);
    }

    public async Task<string> GetSubscriptionStateAsync(string subscriptionId, string connectionId)
    {
        try
        {
            QuerySubscription? subscription = _subscriptionManager.GetSubscription(subscriptionId);
            if (subscription == null)
            {
                return CreateErrorResponse("Subscription not found.");
            }

            QueryResult result = subscription.GetCurrentState();
            return result.ToJson();
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Failed to get subscription state: {ex.Message}");
        }
    }

    public void OnClientDisconnected(string connectionId)
    {
        // Find and remove all subscriptions for this connection
        var subscriptionsToRemove = _subscriptionToConnectionId
            .Where(kvp => kvp.Value == connectionId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (string subscriptionId in subscriptionsToRemove)
        {
            _subscriptionManager.Unsubscribe(subscriptionId);
            _subscriptionToConnectionId.Remove(subscriptionId);
        }
    }

    private async Task SendUpdateToClient(string connectionId, string subscriptionId, QueryUpdate update)
    {
        try
        {
            string updateJson = update.ToJson();
            await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveSubscriptionUpdateAsync", subscriptionId, updateJson);
        }
        catch
        {
            // Client might have disconnected
        }
    }

    private async Task SendErrorToClient(string connectionId, string error)
    {
        try
        {
            string errorJson = JsonSerializer.Serialize(new { error, timestamp = DateTime.UtcNow });
            await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveQueryErrorAsync", errorJson);
        }
        catch
        {
            // Client might have disconnected
        }
    }

    private static string CreateErrorResponse(string error)
    {
        return JsonSerializer.Serialize(new { success = false, error, timestamp = DateTime.UtcNow });
    }

    private Mutation? ParseMutation(Dictionary<string, object> mutationDict)
    {
        if (!mutationDict.TryGetValue("type", out object? typeObj) || typeObj is not string mutationType)
        {
            return null;
        }

        try
        {
            return mutationType switch
            {
                "UpdateProperty" => ParseUpdatePropertyMutation(mutationDict),
                "AddToCollection" => ParseAddToCollectionMutation(mutationDict),
                "RemoveFromCollection" => ParseRemoveFromCollectionMutation(mutationDict),
                "CreateObject" => ParseCreateObjectMutation(mutationDict),
                "DeleteObject" => ParseDeleteObjectMutation(mutationDict),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private Mutation? ParseUpdatePropertyMutation(Dictionary<string, object> dict)
    {
        if (!dict.TryGetValue("targetId", out object? targetIdObj) || !Guid.TryParse(targetIdObj?.ToString(), out Guid targetId))
            return null;

        if (!dict.TryGetValue("propertyName", out object? propObj) || propObj is not string propertyName)
            return null;

        dict.TryGetValue("newValue", out object? newValue);

        return new UpdatePropertyMutation(targetId, propertyName, newValue);
    }

    private Mutation? ParseAddToCollectionMutation(Dictionary<string, object> dict)
    {
        if (!dict.TryGetValue("targetId", out object? targetIdObj) || !Guid.TryParse(targetIdObj?.ToString(), out Guid targetId))
            return null;

        if (!dict.TryGetValue("collectionPropertyName", out object? propObj) || propObj is not string propertyName)
            return null;

        if (!dict.TryGetValue("item", out object? item))
            return null;

        int? index = null;
        if (dict.TryGetValue("index", out object? indexObj) && int.TryParse(indexObj?.ToString(), out int parsedIndex))
        {
            index = parsedIndex;
        }

        return new AddToCollectionMutation(targetId, propertyName, item, index);
    }

    private Mutation? ParseRemoveFromCollectionMutation(Dictionary<string, object> dict)
    {
        if (!dict.TryGetValue("targetId", out object? targetIdObj) || !Guid.TryParse(targetIdObj?.ToString(), out Guid targetId))
            return null;

        if (!dict.TryGetValue("collectionPropertyName", out object? propObj) || propObj is not string propertyName)
            return null;

        if (!dict.TryGetValue("index", out object? indexObj) || !int.TryParse(indexObj?.ToString(), out int index))
            return null;

        return new RemoveFromCollectionMutation(targetId, propertyName, index);
    }

    private Mutation? ParseCreateObjectMutation(Dictionary<string, object> dict)
    {
        if (!dict.TryGetValue("parentId", out object? parentIdObj) || !Guid.TryParse(parentIdObj?.ToString(), out Guid parentId))
            return null;

        if (!dict.TryGetValue("propertyName", out object? propObj) || propObj is not string propertyName)
            return null;

        if (!dict.TryGetValue("typeName", out object? typeObj) || typeObj is not string typeName)
            return null;

        Dictionary<string, object?>? initialValues = null;
        if (dict.TryGetValue("initialValues", out object? valuesObj) && valuesObj is JsonElement jsonElement)
        {
            initialValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonElement.GetRawText());
        }

        return new CreateObjectMutation(parentId, propertyName, typeName, initialValues);
    }

    private Mutation? ParseDeleteObjectMutation(Dictionary<string, object> dict)
    {
        if (!dict.TryGetValue("targetId", out object? targetIdObj) || !Guid.TryParse(targetIdObj?.ToString(), out Guid targetId))
            return null;

        return new DeleteObjectMutation(targetId);
    }
}
