namespace Beutl.Protocol.Queries;

public interface IQueryClient
{
    Task<QueryResult?> ExecuteQueryAsync(QuerySchema schema, Guid? targetId = null);

    Task<MutationResult?> ExecuteMutationAsync(Mutation mutation, QuerySchema? returnSchema = null);

    Task<IObservable<QueryUpdate>?> SubscribeAsync(string subscriptionId, QuerySchema schema, Guid? targetId = null);

    Task<bool> UnsubscribeAsync(string subscriptionId);

    Task<QueryResult?> GetSubscriptionStateAsync(string subscriptionId);
}
