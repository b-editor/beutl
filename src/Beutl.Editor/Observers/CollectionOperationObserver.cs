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
    // The list's items as of the last notification. A Reset carries no items, and another notification may not
    // describe the change exactly, so this is the only reliable record of what the list held before it.
    private readonly List<T> _snapshot = [];
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

        foreach (CoreObject item in list.OfType<CoreObject>())
        {
            InitializeChildPublishers(item);
        }

        if (list is INotifyCollectionChanged notifyCollection)
        {
            // Only a list that notifies its changes can raise a Reset, so only such a list needs the copy.
            _snapshot.AddRange(list);
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

    private void DisposeChildPublisher(ICoreObject obj)
    {
        if (_childPublishers.Remove(obj, out CoreObjectOperationObserver? childPublisher))
        {
            childPublisher.Dispose();
        }
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
        // A Reset carries no items, and a notification that does not describe the change cannot be applied
        // item by item: CoreList.AddRange(ReadOnlySpan<T>) notifies its pooled array, which is longer than the
        // items it added and can hold stale ones. Both are handled by comparing the snapshot with the list.
        bool describesChange = DescribesChange(e);
        T[] oldItems = describesChange ? [] : _snapshot.ToArray();

        // The snapshot and the child publishers follow every change, including one made while publishing
        // is suppressed (KeyFrameAnimation re-sorts its key frames that way). Otherwise a later Reset would
        // report stale items, and re-adding an item whose publisher was kept would throw.
        if (describesChange)
        {
            ApplyToSnapshot(e);
            ApplyToChildPublishers(e);
        }
        else
        {
            _snapshot.Clear();
            _snapshot.AddRange(_list);
            MatchChildPublishersToSnapshot();
        }

        // Suppress publishing during remote operation application to prevent echo-back
        if (PublishingSuppression.IsSuppressed)
        {
            return;
        }

        switch (describesChange ? e.Action : NotifyCollectionChangedAction.Reset)
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
            case NotifyCollectionChangedAction.Reset:
                EnqueueReset(oldItems);
                break;
        }
    }

    // Whether the notification can be applied to the snapshot item by item:
    // its indexes fit, and the result is as long as the list.
    private bool DescribesChange(NotifyCollectionChangedEventArgs e)
    {
        int count = _snapshot.Count;
        int oldCount = e.OldItems?.Count ?? 0;
        int newCount = e.NewItems?.Count ?? 0;
        return e.Action switch
        {
            NotifyCollectionChangedAction.Add =>
                e.NewItems != null
                && IsInRange(e.NewStartingIndex, count)
                && count + newCount == _list.Count,
            NotifyCollectionChangedAction.Remove =>
                e.OldItems != null
                && IsInRange(e.OldStartingIndex, count - oldCount)
                && count - oldCount == _list.Count,
            NotifyCollectionChangedAction.Replace =>
                e.OldItems != null
                && e.NewItems != null
                && IsInRange(e.OldStartingIndex, count - oldCount)
                && IsInRange(e.NewStartingIndex, count - oldCount)
                && count - oldCount + newCount == _list.Count,
            NotifyCollectionChangedAction.Move =>
                e.OldItems != null
                && IsInRange(e.OldStartingIndex, count - oldCount)
                && IsInRange(e.NewStartingIndex, count - oldCount)
                && count == _list.Count,
            _ => false
        };

        static bool IsInRange(int index, int max) => index >= 0 && index <= max;
    }

    private void ApplyToSnapshot(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                _snapshot.InsertRange(e.NewStartingIndex, e.NewItems!.Cast<T>());
                break;
            case NotifyCollectionChangedAction.Remove:
                _snapshot.RemoveRange(e.OldStartingIndex, e.OldItems!.Count);
                break;
            case NotifyCollectionChangedAction.Replace:
                _snapshot.RemoveRange(e.OldStartingIndex, e.OldItems!.Count);
                _snapshot.InsertRange(e.NewStartingIndex, e.NewItems!.Cast<T>());
                break;
            case NotifyCollectionChangedAction.Move:
                List<T> movedItems = _snapshot.GetRange(e.OldStartingIndex, e.OldItems!.Count);
                _snapshot.RemoveRange(e.OldStartingIndex, movedItems.Count);
                _snapshot.InsertRange(e.NewStartingIndex, movedItems);
                break;
        }
    }

    private void ApplyToChildPublishers(NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move)
        {
            return;
        }

        foreach (CoreObject oldItem in e.OldItems?.OfType<CoreObject>() ?? [])
        {
            DisposeChildPublisher(oldItem);
        }

        foreach (CoreObject newItem in e.NewItems?.OfType<CoreObject>() ?? [])
        {
            InitializeChildPublishers(newItem);
        }
    }

    // Keeps the publishers of the items that are still in the list.
    private void MatchChildPublishersToSnapshot()
    {
        var items = new HashSet<ICoreObject>(_snapshot.OfType<CoreObject>());
        foreach (ICoreObject staleItem in _childPublishers.Keys.Where(key => !items.Contains(key)).ToArray())
        {
            DisposeChildPublisher(staleItem);
        }

        foreach (CoreObject item in _snapshot.OfType<CoreObject>())
        {
            if (!_childPublishers.ContainsKey(item))
            {
                InitializeChildPublishers(item);
            }
        }
    }

    private void EnqueueAdds(NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null)
        {
            return;
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

    // A Reset, or a notification that does not describe the change, is recorded as the removal of every item
    // the list held, followed by the insertion of every item it holds now. For Clear and Replace, these are
    // the operations a list with ResetBehavior.Remove records.
    private void EnqueueReset(T[] oldItems)
    {
        T[] newItems = [.. _snapshot];
        if (oldItems.SequenceEqual(newItems))
        {
            return;
        }

        if (oldItems.Length > 0)
        {
            var operation = new RemoveCollectionRangeOperation<T>
            {
                SequenceNumber = _sequenceNumberGenerator.GetNext(),
                Object = _owner,
                PropertyPath = _propertyPath,
                Index = 0,
                Items = oldItems
            };
            _operations.OnNext(operation);
        }

        if (newItems.Length > 0)
        {
            var operation = new InsertCollectionRangeOperation<T>
            {
                SequenceNumber = _sequenceNumberGenerator.GetNext(),
                Object = _owner,
                PropertyPath = _propertyPath,
                Items = newItems,
                Index = 0
            };
            _operations.OnNext(operation);
        }
    }
}
