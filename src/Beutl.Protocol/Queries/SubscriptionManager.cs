using Beutl.Protocol.Operations;

namespace Beutl.Protocol.Queries;

public class SubscriptionManager : IDisposable
{
    private readonly Dictionary<string, QuerySubscription> _subscriptions = new();
    private readonly OperationSequenceGenerator _sequenceGenerator;
    private bool _disposed;

    public SubscriptionManager(OperationSequenceGenerator? sequenceGenerator = null)
    {
        _sequenceGenerator = sequenceGenerator ?? new OperationSequenceGenerator();
    }

    public QuerySubscription Subscribe(string subscriptionId, ICoreObject target, QuerySchema schema)
    {
        if (_subscriptions.ContainsKey(subscriptionId))
        {
            throw new InvalidOperationException($"Subscription with ID '{subscriptionId}' already exists.");
        }

        var subscription = new QuerySubscription(target, schema, _sequenceGenerator);
        _subscriptions[subscriptionId] = subscription;

        return subscription;
    }

    public QuerySubscription? GetSubscription(string subscriptionId)
    {
        _subscriptions.TryGetValue(subscriptionId, out QuerySubscription? subscription);
        return subscription;
    }

    public bool Unsubscribe(string subscriptionId)
    {
        if (_subscriptions.Remove(subscriptionId, out QuerySubscription? subscription))
        {
            subscription.Dispose();
            return true;
        }

        return false;
    }

    public IEnumerable<string> GetActiveSubscriptionIds()
    {
        return _subscriptions.Keys;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (QuerySubscription subscription in _subscriptions.Values)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }
}
