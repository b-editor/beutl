using System.Reactive.Linq;
using System.Reactive.Subjects;
using Beutl.Protocol.Operations;
using Beutl.Protocol.Synchronization;

namespace Beutl.Protocol.Queries;

public class QuerySubscription : IDisposable
{
    private readonly ICoreObject _target;
    private readonly QuerySchema _schema;
    private readonly QueryExecutor _executor;
    private readonly CoreObjectOperationPublisher _publisher;
    private readonly Subject<QueryUpdate> _updates = new();
    private readonly IDisposable _subscription;
    private bool _disposed;

    public QuerySubscription(
        ICoreObject target,
        QuerySchema schema,
        OperationSequenceGenerator sequenceGenerator)
    {
        _target = target;
        _schema = schema;
        _executor = new QueryExecutor();

        // Create a publisher that will recursively monitor all changes
        _publisher = new CoreObjectOperationPublisher(null, target, sequenceGenerator);

        // Subscribe to operations and convert them to query updates
        _subscription = _publisher.Operations
            .Select(op => CreateQueryUpdate(op))
            .Where(update => update != null)
            .Subscribe(_updates.OnNext!);
    }

    public IObservable<QueryUpdate> Updates => _updates.AsObservable();

    public QueryResult GetCurrentState()
    {
        return _executor.Execute(_target, _schema);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _subscription?.Dispose();
        _publisher?.Dispose();
        _updates.OnCompleted();
    }

    private QueryUpdate? CreateQueryUpdate(SyncOperation operation)
    {
        try
        {
            // Get the updated data based on the schema
            QueryResult currentState = GetCurrentState();

            return new QueryUpdate
            {
                Operation = operation,
                UpdatedData = currentState.Data,
                Timestamp = DateTime.UtcNow
            };
        }
        catch
        {
            // If query execution fails, skip this update
            return null;
        }
    }
}
