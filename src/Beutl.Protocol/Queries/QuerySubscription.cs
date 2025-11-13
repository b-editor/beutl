using System.Collections;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
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
    private HashSet<string> _trackedPropertyPaths;
    private bool _disposed;

    public QuerySubscription(
        ICoreObject target,
        QuerySchema schema,
        OperationSequenceGenerator sequenceNumberGenerator)
    {
        _target = target;
        _schema = schema;
        _executor = new QueryExecutor();
        _trackedPropertyPaths = new HashSet<string>();

        // Build initial tracked property paths by executing the query
        RebuildTrackedPropertyPaths();

        // Create a publisher that will recursively monitor all changes
        _publisher = new CoreObjectOperationPublisher(null, target, sequenceNumberGenerator);

        // Subscribe to operations and convert them to query updates
        // Only process operations that affect properties in the query schema
        _subscription = _publisher.Operations
            .Where(op => IsRelevantOperation(op))
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

    private bool IsRelevantOperation(SyncOperation operation)
    {
        if (operation is not IPropertyPathProvider pathProvider)
        {
            return true;
        }

        string propertyPath = pathProvider.PropertyPath;

        // Check if this property path is being tracked
        bool isTracked = _trackedPropertyPaths.Contains(propertyPath);

        // If not tracked, check if it's a parent path of any tracked property
        // This handles structural changes
        if (!isTracked)
        {
            // Check if this path is a prefix of any tracked path
            // e.g., if "Parent.Child" changes, it might affect "Parent.Child.Name"
            bool affectsTrackedPath = _trackedPropertyPaths.Any(tp =>
                tp.StartsWith(propertyPath + ".") || tp == propertyPath);

            if (affectsTrackedPath)
            {
                return true;
            }
        }

        return isTracked;
    }

    private void RebuildTrackedPropertyPaths()
    {
        var newTrackedPaths = new HashSet<string>();
        CollectTrackedPropertyPaths(_target, _schema.Fields, "", newTrackedPaths);
        _trackedPropertyPaths = newTrackedPaths;
    }

    private void CollectTrackedPropertyPaths(
        object target,
        QueryField[] fields,
        string currentPath,
        HashSet<string> trackedPaths)
    {
        if (target is not ICoreObject)
        {
            return;
        }

        foreach (QueryField field in fields)
        {
            // Build the property path
            string propertyPath = string.IsNullOrEmpty(currentPath)
                ? field.Name
                : $"{currentPath}.{field.Name}";

            // Add this property path to tracked set
            trackedPaths.Add(propertyPath);

            // Get the property value to traverse into sub-objects
            object? value = GetFieldValue(target, field.Name);

            if (value != null && field.HasSubFields)
            {
                ProcessValueForTracking(value, field.SubFields, propertyPath, trackedPaths);
            }
        }
    }

    private void ProcessValueForTracking(
        object value,
        QueryField[] subFields,
        string currentPath,
        HashSet<string> trackedPaths)
    {
        // Handle collections
        if (value is IList list)
        {
            foreach (object? item in list)
            {
                if (item != null)
                {
                    CollectTrackedPropertyPaths(item, subFields, currentPath, trackedPaths);
                }
            }
            return;
        }

        // Handle single objects
        if (value is ICoreObject)
        {
            CollectTrackedPropertyPaths(value, subFields, currentPath, trackedPaths);
        }
    }

    private object? GetFieldValue(object target, string fieldName)
    {
        Type targetType = target.GetType();

        // Try CoreProperty first for ICoreObject
        if (target is ICoreObject coreObject)
        {
            CoreProperty? coreProperty = PropertyRegistry.FindRegistered(coreObject, fieldName);
            if (coreProperty != null)
            {
                return coreObject.GetValue(coreProperty);
            }
        }

        // Try regular property
        PropertyInfo? property = targetType.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null)
        {
            return property.GetValue(target);
        }

        // Try field
        FieldInfo? field = targetType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (field != null)
        {
            return field.GetValue(target);
        }

        return null;
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
