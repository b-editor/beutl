using System.Collections;
using System.ComponentModel;
using System.Reactive.Subjects;
using Beutl.Engine;
using Beutl.Protocol.Operations;
using Beutl.Protocol.Operations.Property;

namespace Beutl.Protocol.Synchronization;

public sealed class CoreObjectOperationPublisher : IOperationPublisher
{
    private readonly ICoreObject _object;
    private readonly string _propertyPath;
    private readonly Dictionary<int, CoreObjectOperationPublisher> _childPublishers = new();
    private readonly Dictionary<int, CollectionOperationPublisher> _collectionPublishers = new();
    private readonly Dictionary<string, IOperationPublisher> _enginePropertyPublishers = new();
    private readonly Subject<SyncOperation> _operations = new();
    private readonly IDisposable? _subscription;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;
    private readonly HashSet<string>? _propertyPathsToTrack;
    private readonly HashSet<string>? _propertiesToTrack;

    public CoreObjectOperationPublisher(
        IObserver<SyncOperation>? observer,
        ICoreObject obj,
        OperationSequenceGenerator sequenceNumberGenerator,
        string propertyPath = "",
        HashSet<string>? propertyPathsToTrack = null)
    {
        _object = obj;
        _propertyPath = propertyPath;
        _sequenceNumberGenerator = sequenceNumberGenerator;
        _propertyPathsToTrack = propertyPathsToTrack;

        if (observer != null)
        {
            _subscription = _operations.Subscribe(observer);
        }

        _propertiesToTrack = _propertyPathsToTrack?.Where(i => i.Contains(_propertyPath))
            .Select(i => i.Substring(_propertyPath.Length).TrimStart('.').Split('.').First())
            .Where(i => !string.IsNullOrEmpty(i))
            .ToHashSet();
        InitializeChildPublishers();
        obj.PropertyChanged += OnPropertyChanged;

        if (obj is EngineObject engineObject)
        {
            InitializeEnginePropertyPublishers(engineObject);
        }
    }

    public IObservable<SyncOperation> Operations => _operations;

    public void Dispose()
    {
        _object.PropertyChanged -= OnPropertyChanged;

        foreach (CoreObjectOperationPublisher child in _childPublishers.Values)
        {
            child.Dispose();
        }

        foreach (CollectionOperationPublisher collection in _collectionPublishers.Values)
        {
            collection.Dispose();
        }

        foreach (IOperationPublisher enginePublisher in _enginePropertyPublishers.Values)
        {
            enginePublisher.Dispose();
        }

        _subscription?.Dispose();
        _operations.OnCompleted();
    }

    private string BuildPropertyPath(string propertyName)
    {
        return string.IsNullOrEmpty(_propertyPath)
            ? propertyName
            : $"{_propertyPath}.{propertyName}";
    }

    private void InitializeChildPublishers()
    {
        foreach (CoreProperty property in PropertyRegistry.GetRegistered(_object.GetType()))
        {
            if (Hierarchical.HierarchicalParentProperty.Id == property.Id) continue;
            if (_propertiesToTrack != null && !_propertiesToTrack.Contains(property.Name))
            {
                continue;
            }

            object? value = _object.GetValue(property);
            string childPath = BuildPropertyPath(property.Name);

            if (value is IList list)
            {
                var collectionPublisher = new CollectionOperationPublisher(
                    _operations,
                    list,
                    _object,
                    childPath,
                    _sequenceNumberGenerator,
                    _propertyPathsToTrack);
                _collectionPublishers.Add(property.Id, collectionPublisher);
            }
            else if (value is ICoreObject child)
            {
                var childPublisher = new CoreObjectOperationPublisher(
                    _operations,
                    child,
                    _sequenceNumberGenerator,
                    childPath,
                    _propertyPathsToTrack);
                _childPublishers.Add(property.Id, childPublisher);
            }
        }
    }

    private void InitializeEnginePropertyPublishers(EngineObject engineObject)
    {
        foreach (IProperty property in engineObject.Properties)
        {
            if (_propertiesToTrack != null && !_propertiesToTrack.Contains(property.Name))
            {
                continue;
            }

            string childPath = BuildPropertyPath(property.Name);
            var publisherType = typeof(EnginePropertyOperationPublisher<>).MakeGenericType(property.ValueType);
            var publisher = (IOperationPublisher)Activator.CreateInstance(
                publisherType,
                _operations,
                engineObject,
                property,
                _sequenceNumberGenerator,
                childPath,
                _propertyPathsToTrack)!;

            _enginePropertyPublishers.Add(property.Name, publisher);
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Suppress publishing during remote operation application to prevent echo-back
        if (PublishingSuppression.IsSuppressed)
        {
            return;
        }

        if (e is not CorePropertyChangedEventArgs args ||
            sender is not ICoreObject source ||
            args.Property.Id == Hierarchical.HierarchicalParentProperty.Id)
        {
            return;
        }

        if (_propertiesToTrack != null &&
            !_propertiesToTrack.Contains(args.Property.Name))
        {
            return;
        }

        if (_childPublishers.Remove(args.Property.Id, out var childPublisher))
        {
            childPublisher.Dispose();
        }

        if (_collectionPublishers.Remove(args.Property.Id, out var collectionPublisher))
        {
            collectionPublisher.Dispose();
        }

        string childPath = BuildPropertyPath(args.Property.Name);

        if (args.NewValue is IList list)
        {
            var newPublisher = new CollectionOperationPublisher(
                _operations,
                list,
                source,
                childPath,
                _sequenceNumberGenerator,
                _propertyPathsToTrack);
            _collectionPublishers.Add(args.Property.Id, newPublisher);
        }
        else if (args.NewValue is ICoreObject newChild)
        {
            var newPublisher = new CoreObjectOperationPublisher(
                _operations,
                newChild,
                _sequenceNumberGenerator,
                childPath,
                _propertyPathsToTrack);
            _childPublishers.Add(args.Property.Id, newPublisher);
        }

        var operation = UpdatePropertyValueOperation.Create(
            source,
            args.Property,
            childPath,
            args.NewValue,
            _sequenceNumberGenerator);
        _operations.OnNext(operation);
    }
}
