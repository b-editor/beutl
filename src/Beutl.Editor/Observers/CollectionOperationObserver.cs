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
    // The list's items as of the last notification. A Reset notification carries no items,
    // so this is the only record of what a Reset removed.
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
        T[] resetItems = e.Action == NotifyCollectionChangedAction.Reset ? _snapshot.ToArray() : [];

        // The snapshot and the child publishers follow every change, including one made while publishing
        // is suppressed (KeyFrameAnimation re-sorts its key frames that way). Otherwise a later Reset would
        // report stale items, and re-adding an item whose publisher was kept would throw.
        UpdateSnapshot(e);
        UpdateChildPublishers(e);

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
            case NotifyCollectionChangedAction.Reset:
                EnqueueReset(resetItems);
                break;
        }
    }

    private void UpdateSnapshot(NotifyCollectionChangedEventArgs e)
    {
        bool updated = e.Action switch
        {
            NotifyCollectionChangedAction.Add => TryInsertIntoSnapshot(e.NewStartingIndex, e.NewItems),
            NotifyCollectionChangedAction.Remove => TryRemoveFromSnapshot(e.OldStartingIndex, e.OldItems),
            NotifyCollectionChangedAction.Move or NotifyCollectionChangedAction.Replace =>
                TryRemoveFromSnapshot(e.OldStartingIndex, e.OldItems)
                && TryInsertIntoSnapshot(e.NewStartingIndex, e.NewItems),
            _ => false
        };

        // Copy the list after a Reset, and after a notification that does not match the change.
        // For example, CoreList.AddRange(ReadOnlySpan<T>) reports its pooled array,
        // which can be longer than the items it added.
        if (!updated || _snapshot.Count != _list.Count)
        {
            _snapshot.Clear();
            _snapshot.AddRange(_list);
        }
    }

    private bool TryInsertIntoSnapshot(int index, IList? items)
    {
        if (items == null || index < 0 || index > _snapshot.Count)
        {
            return false;
        }

        _snapshot.InsertRange(index, items.Cast<T>());
        return true;
    }

    private bool TryRemoveFromSnapshot(int index, IList? items)
    {
        if (items == null || index < 0 || index > _snapshot.Count - items.Count)
        {
            return false;
        }

        _snapshot.RemoveRange(index, items.Count);
        return true;
    }

    private void UpdateChildPublishers(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                foreach (CoreObject oldItem in e.OldItems?.OfType<CoreObject>() ?? [])
                {
                    DisposeChildPublisher(oldItem);
                }

                foreach (CoreObject newItem in e.NewItems?.OfType<CoreObject>() ?? [])
                {
                    InitializeChildPublishers(newItem);
                }

                break;
            case NotifyCollectionChangedAction.Reset:
                // Match the publishers to the list again, keeping those of the items that are still in it.
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

                break;
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

    // A Reset is recorded as the removal of every item the list held, followed by the insertion of every item
    // it holds now. For Clear and Replace, these are the operations a list with ResetBehavior.Remove records.
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
