using System.Collections;
using System.Reactive.Subjects;
using System.Reflection;
using Beutl.Protocol.Operations;

namespace Beutl.Protocol.Queries;

public class LocalQueryClient : IAsyncDisposable, IQueryClient
{
    private readonly ICoreObject _root;
    private readonly OperationSequenceGenerator _sequenceGenerator;
    private readonly QueryExecutor _queryExecutor;
    private readonly MutationExecutor _mutationExecutor;
    private readonly Dictionary<string, QuerySubscription> _subscriptions = new();
    private readonly Subject<string> _errors = new();
    private bool _disposed;

    public LocalQueryClient(ICoreObject root, OperationSequenceGenerator? sequenceGenerator = null)
    {
        _root = root;
        _sequenceGenerator = sequenceGenerator ?? new OperationSequenceGenerator();
        _queryExecutor = new QueryExecutor();
        _mutationExecutor = new MutationExecutor();
    }

    public IObservable<string> Errors => _errors;

    public Task<QueryResult?> ExecuteQueryAsync(QuerySchema schema, Guid? targetId = null)
    {
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalQueryClient));
            }

            QueryResult? result = targetId.HasValue
                ? _queryExecutor.ExecuteById(_root, targetId.Value, schema)
                : _queryExecutor.Execute(_root, schema);

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _errors.OnNext($"Query execution failed: {ex.Message}");
            return Task.FromResult<QueryResult?>(null);
        }
    }

    public Task<MutationResult?> ExecuteMutationAsync(Mutation mutation, QuerySchema? returnSchema = null)
    {
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalQueryClient));
            }

            MutationResult result = _mutationExecutor.Execute(_root, mutation, returnSchema);
            return Task.FromResult<MutationResult?>(result);
        }
        catch (Exception ex)
        {
            _errors.OnNext($"Mutation execution failed: {ex.Message}");
            return Task.FromResult<MutationResult?>(null);
        }
    }

    public Task<IObservable<QueryUpdate>?> SubscribeAsync(string subscriptionId, QuerySchema schema, Guid? targetId = null)
    {
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalQueryClient));
            }

            if (_subscriptions.ContainsKey(subscriptionId))
            {
                _errors.OnNext($"Subscription '{subscriptionId}' already exists.");
                return Task.FromResult<IObservable<QueryUpdate>?>(null);
            }

            // Find the target object if targetId is specified
            ICoreObject target = _root;
            if (targetId.HasValue && targetId.Value != _root.Id)
            {
                ICoreObject? foundTarget = FindObjectById(_root, targetId.Value);
                if (foundTarget == null)
                {
                    _errors.OnNext($"Target object with ID '{targetId}' not found.");
                    return Task.FromResult<IObservable<QueryUpdate>?>(null);
                }
                target = foundTarget;
            }

            var subscription = new QuerySubscription(target, schema, _sequenceGenerator);
            _subscriptions[subscriptionId] = subscription;

            return Task.FromResult<IObservable<QueryUpdate>?>(subscription.Updates);
        }
        catch (Exception ex)
        {
            _errors.OnNext($"Subscription failed: {ex.Message}");
            return Task.FromResult<IObservable<QueryUpdate>?>(null);
        }
    }

    public Task<bool> UnsubscribeAsync(string subscriptionId)
    {
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalQueryClient));
            }

            if (_subscriptions.Remove(subscriptionId, out QuerySubscription? subscription))
            {
                subscription.Dispose();
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _errors.OnNext($"Unsubscribe failed: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public Task<QueryResult?> GetSubscriptionStateAsync(string subscriptionId)
    {
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalQueryClient));
            }

            if (_subscriptions.TryGetValue(subscriptionId, out QuerySubscription? subscription))
            {
                QueryResult currentState = subscription.GetCurrentState();
                return Task.FromResult<QueryResult?>(currentState);
            }

            _errors.OnNext($"Subscription '{subscriptionId}' not found.");
            return Task.FromResult<QueryResult?>(null);
        }
        catch (Exception ex)
        {
            _errors.OnNext($"Get subscription state failed: {ex.Message}");
            return Task.FromResult<QueryResult?>(null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (QuerySubscription subscription in _subscriptions.Values)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();
        _errors.OnCompleted();

        await Task.CompletedTask;
    }

    private ICoreObject? FindObjectById(ICoreObject root, Guid id)
    {
        if (root.Id == id)
        {
            return root;
        }

        // Search through all properties
        Type type = root.GetType();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? value = property.GetValue(root);
            if (value == null) continue;

            // Check if the property itself is the target
            if (value is ICoreObject coreObject)
            {
                ICoreObject? found = FindObjectById(coreObject, id);
                if (found != null) return found;
            }
            // Check if it's a collection of ICoreObjects
            else if (value is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    if (item is ICoreObject coreObjectItem)
                    {
                        ICoreObject? found = FindObjectById(coreObjectItem, id);
                        if (found != null) return found;
                    }
                }
            }
        }

        return null;
    }
}
