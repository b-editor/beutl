using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Subjects;
using Beutl.Editor.Infrastructure;
using Beutl.Editor.Operations;
using Beutl.Serialization;

namespace Beutl.Editor.Observers;

public sealed class CollectionOperationObserver<T> : IOperationObserver
{
    private readonly IList<T> _list;
    private readonly CoreObject _owner;
    private readonly string _propertyPath;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;
    private readonly Dictionary<ICoreObject, CoreObjectOperationObserver> _childPublishers = new();
    private readonly Subject<ChangeOperation> _operations = new();
    private readonly IDisposable _subscription;
    private readonly HashSet<string>? _propertyPathsToTrack;

    public CollectionOperationObserver(IObserver<ChangeOperation> observer,
        IList<T> list,
        CoreObject owner,
        string propertyPath,
        OperationSequenceGenerator sequenceNumberGenerator,
        HashSet<string>? propertyPathsToTrack = null)
    {
        _list = list;
        _owner = owner;
        _propertyPath = propertyPath;
        _sequenceNumberGenerator = sequenceNumberGenerator;
        _subscription = _operations.Subscribe(observer);
        _propertyPathsToTrack = propertyPathsToTrack;

        foreach (T item in list)
        {
            if (item is CoreObject coreObject)
            {
                InitializeChildPublishers(coreObject);
            }
        }

        if (list is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged += OnCollectionChanged;
        }
    }

    public IObservable<ChangeOperation> Operations => _operations;

    private void InitializeChildPublishers(ICoreObject obj)
    {
        var childPublisher = new CoreObjectOperationObserver(
            _operations,
            obj,
            _sequenceNumberGenerator,
            _propertyPath,
            _propertyPathsToTrack);
        _childPublishers.Add(obj, childPublisher);
    }

    public void Dispose()
    {
        foreach (var childPublisher in _childPublishers.Values)
        {
            childPublisher.Dispose();
        }

        _childPublishers.Clear();
        if (_list is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged -= OnCollectionChanged;
        }

        _subscription.Dispose();
        _operations.OnCompleted();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Suppress publishing during remote operation application to prevent echo-back
        if (PublishingSuppression.IsSuppressed)
        {
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                EnqueueAdds(e);
                break;
            case NotifyCollectionChangedAction.Remove:
                EnqueueRemoveRange(e);
                break;
            case NotifyCollectionChangedAction.Move:
                EnqueueMove(e);
                break;
            case NotifyCollectionChangedAction.Replace:
                EnqueueReplace(e);
                break;
        }
    }

    private void EnqueueAdds(NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null)
        {
            return;
        }

        foreach (object newItem in e.NewItems)
        {
            if (newItem is CoreObject coreObject)
            {
                InitializeChildPublishers(coreObject);
            }
        }

        int index = e.NewStartingIndex;
        var operation = new InsertCollectionRangeOperation<T>
        {
            SequenceNumber = _sequenceNumberGenerator.GetNext(),
            Object = _owner,
            PropertyPath = _propertyPath,
            Items = e.NewItems.Cast<T>().ToArray(),
            Index = index
        };
        _operations.OnNext(operation);
    }

    private void EnqueueRemoveRange(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems == null)
        {
            return;
        }

        foreach (object oldItem in e.OldItems)
        {
            if (oldItem is CoreObject coreObject)
            {
                if (_childPublishers.TryGetValue(coreObject, out var childPublisher))
                {
                    childPublisher.Dispose();
                    _childPublishers.Remove(coreObject);
                }
            }
        }

        var operation = new RemoveCollectionRangeOperation<T>
        {
            SequenceNumber = _sequenceNumberGenerator.GetNext(),
            Object = _owner,
            PropertyPath = _propertyPath,
            Index = e.OldStartingIndex,
            Items = e.OldItems.Cast<T>().ToArray()
        };
        _operations.OnNext(operation);
    }

    private void EnqueueMove(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems == null)
        {
            return;
        }

        var operation = new MoveCollectionRangeOperation<T>
        {
            SequenceNumber = _sequenceNumberGenerator.GetNext(),
            Object = _owner,
            PropertyPath = _propertyPath,
            OldIndex = e.OldStartingIndex,
            NewIndex = e.NewStartingIndex,
            Count = e.OldItems.Count
        };
        _operations.OnNext(operation);
    }

    private void EnqueueReplace(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (object oldItem in e.OldItems)
            {
                if (oldItem is CoreObject coreObject)
                {
                    if (_childPublishers.TryGetValue(coreObject, out var childPublisher))
                    {
                        childPublisher.Dispose();
                        _childPublishers.Remove(coreObject);
                    }
                }
            }

            var operation = new RemoveCollectionRangeOperation<T>
            {
                SequenceNumber = _sequenceNumberGenerator.GetNext(),
                Object = _owner,
                PropertyPath = _propertyPath,
                Index = e.OldStartingIndex,
                Items = e.OldItems.Cast<T>().ToArray()
            };
            _operations.OnNext(operation);
        }

        if (e.NewItems != null)
        {
            foreach (var newItem in e.NewItems)
            {
                if (newItem is CoreObject coreObject)
                {
                    InitializeChildPublishers(coreObject);
                }
            }

            int index = e.NewStartingIndex;
            var operation = new InsertCollectionRangeOperation<T>
            {
                SequenceNumber = _sequenceNumberGenerator.GetNext(),
                Object = _owner,
                PropertyPath = _propertyPath,
                Items = e.NewItems.Cast<T>().ToArray(),
                Index = index
            };
            _operations.OnNext(operation);
        }
    }
}
