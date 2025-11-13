namespace Beutl.Protocol.Queries;

public interface IQueryClient
{
    Task ReceiveSubscriptionUpdateAsync(string subscriptionId, string updateJson);

    Task ReceiveQueryErrorAsync(string errorJson);
}
