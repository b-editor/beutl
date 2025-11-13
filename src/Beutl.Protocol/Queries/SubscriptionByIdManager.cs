namespace Beutl.Protocol.Queries;

public class SubscriptionByIdManager : IDisposable
{
    private readonly ICoreObject _root;
    private readonly SubscriptionManager _subscriptionManager;
    private bool _disposed;

    public SubscriptionByIdManager(ICoreObject root)
    {
        _root = root;
        _subscriptionManager = new SubscriptionManager();
    }

    public QuerySubscription? SubscribeById(string subscriptionId, Guid targetId, QuerySchema schema)
    {
        ICoreObject? target = _root.FindById(targetId);
        if (target == null)
        {
            return null;
        }

        return _subscriptionManager.Subscribe(subscriptionId, target, schema);
    }

    public QuerySubscription? GetSubscription(string subscriptionId)
    {
        return _subscriptionManager.GetSubscription(subscriptionId);
    }

    public bool Unsubscribe(string subscriptionId)
    {
        return _subscriptionManager.Unsubscribe(subscriptionId);
    }

    public IEnumerable<string> GetActiveSubscriptionIds()
    {
        return _subscriptionManager.GetActiveSubscriptionIds();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _subscriptionManager.Dispose();
    }
}
