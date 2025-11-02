using System.Collections;
using System.ComponentModel;
using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Protocol.Operations;
using Beutl.Protocol.Operations.Collections;
using Beutl.Protocol.Operations.Property;

namespace Beutl.Protocol.Synchronization;

public sealed class CoreObjectOperationPublisher : IOperationPublisher
{
    private readonly ICoreObject _object;
    private readonly Dictionary<int, CoreObjectOperationPublisher> _childPublishers = new();
    private readonly Dictionary<int, CollectionOperationPublisher> _collectionPublishers = new();
    private readonly Dictionary<string, IOperationPublisher> _enginePropertyPublishers = new();
    private readonly Subject<SyncOperation> _operations = new();
    private readonly IDisposable? _subscription;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;

    public CoreObjectOperationPublisher(
        IObserver<SyncOperation>? observer,
        ICoreObject obj,
        OperationSequenceGenerator sequenceNumberGenerator)
    {
        _object = obj;
        _sequenceNumberGenerator = sequenceNumberGenerator;

        if (observer != null)
        {
            _subscription = _operations.Subscribe(observer);
        }

        InitializeChildPublishers(obj, sequenceNumberGenerator);
        obj.PropertyChanged += OnPropertyChanged;

        if (obj is EngineObject engineObject)
        {
            InitializeEnginePropertyPublishers(engineObject, sequenceNumberGenerator);
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

    private void InitializeChildPublishers(ICoreObject obj, OperationSequenceGenerator sequenceNumberGenerator)
    {
        foreach (CoreProperty property in PropertyRegistry.GetRegistered(obj.GetType()))
        {
            object? value = obj.GetValue(property);

            if (value is IList list)
            {
                var collectionPublisher = new CollectionOperationPublisher(
                    _operations,
                    list,
                    obj,
                    property.Name,
                    sequenceNumberGenerator);
                _collectionPublishers.Add(property.Id, collectionPublisher);
            }
            else if (value is ICoreObject child)
            {
                var childPublisher = new CoreObjectOperationPublisher(
                    _operations,
                    child,
                    sequenceNumberGenerator);
                _childPublishers.Add(property.Id, childPublisher);
            }
        }
    }

    private void InitializeEnginePropertyPublishers(EngineObject engineObject, OperationSequenceGenerator sequenceNumberGenerator)
    {
        foreach (IProperty property in engineObject.Properties)
        {
            var publisherType = typeof(EnginePropertyOperationPublisher<>).MakeGenericType(property.ValueType);
            var publisher = (IOperationPublisher)Activator.CreateInstance(
                publisherType,
                _operations,
                engineObject,
                property,
                sequenceNumberGenerator)!;

            _enginePropertyPublishers.Add(property.Name, publisher);
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is not CorePropertyChangedEventArgs args ||
            sender is not ICoreObject source ||
            args.Property.Id == Hierarchical.HierarchicalParentProperty.Id)
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

        if (args.NewValue is IList list)
        {
            var newPublisher = new CollectionOperationPublisher(
                _operations,
                list,
                source,
                args.Property.Name,
                _sequenceNumberGenerator);
            _collectionPublishers.Add(args.Property.Id, newPublisher);
        }
        else if (args.NewValue is ICoreObject newChild)
        {
            var newPublisher = new CoreObjectOperationPublisher(
                _operations,
                newChild,
                new OperationSequenceGenerator());
            _childPublishers.Add(args.Property.Id, newPublisher);
        }

        var operation = UpdatePropertyValueOperation.Create(
            source,
            args.Property,
            args.NewValue,
            _sequenceNumberGenerator);
        _operations.OnNext(operation);
    }
}
