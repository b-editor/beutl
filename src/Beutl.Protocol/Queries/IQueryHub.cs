namespace Beutl.Protocol.Queries;

public interface IQueryHub
{
    Task<string> ExecuteQueryAsync(string queryJson, string? targetId = null);

    Task<string> ExecuteMutationAsync(string mutationJson, string? returnSchemaJson = null);

    Task<bool> SubscribeAsync(string subscriptionId, string queryJson, string? targetId = null);

    Task<bool> UnsubscribeAsync(string subscriptionId);

    Task<string> GetSubscriptionStateAsync(string subscriptionId);
}
