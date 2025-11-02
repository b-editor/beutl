using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Subjects;
using Beutl.Protocol.Operations;
using Beutl.Protocol.Operations.Collections;

namespace Beutl.Protocol.Synchronization;

public sealed class CollectionOperationPublisher : IOperationPublisher
{
    private readonly IList _list;
    private readonly ICoreObject _owner;
    private readonly string _propertyName;
    private readonly OperationSequenceGenerator _sequenceNumberGenerator;
    private readonly Subject<SyncOperation> _operations = new();
    private readonly IDisposable _subscription;

    public CollectionOperationPublisher(
        IObserver<SyncOperation> observer,
        IList list,
        ICoreObject owner,
        string propertyName,
        OperationSequenceGenerator sequenceNumberGenerator)
    {
        _list = list;
        _owner = owner;
        _propertyName = propertyName;
        _sequenceNumberGenerator = sequenceNumberGenerator;
        _subscription = _operations.Subscribe(observer);

        if (list is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged += OnCollectionChanged;
        }
    }

    public IObservable<SyncOperation> Operations => _operations;

    public void Dispose()
    {
        if (_list is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged -= OnCollectionChanged;
        }

        _subscription.Dispose();
        _operations.OnCompleted();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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

        int index = e.NewStartingIndex;
        foreach (ICoreObject newItem in e.NewItems.OfType<ICoreObject>())
        {
            var json = CoreSerializerHelper.SerializeToJsonObject(newItem);
            var operation = new InsertCollectionItemOperation
            {
                SequenceNumber = _sequenceNumberGenerator.GetNext(),
                ObjectId = _owner.Id,
                PropertyName = _propertyName,
                Item = json,
                Index = index++
            };
            _operations.OnNext(operation);
        }
    }

    private void EnqueueRemoveRange(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems == null)
        {
            return;
        }

        var operation = new RemoveCollectionRangeOperation
        {
            SequenceNumber = _sequenceNumberGenerator.GetNext(),
            ObjectId = _owner.Id,
            PropertyName = _propertyName,
            Index = e.OldStartingIndex,
            Count = e.OldItems.Count
        };
        _operations.OnNext(operation);
    }

    private void EnqueueMove(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems == null)
        {
            return;
        }

        if (e.OldItems.Count == 1)
        {
            ICoreObject movedItem = (ICoreObject)e.OldItems[0]!;
            var operation = new MoveCollectionItemOperation
            {
                SequenceNumber = _sequenceNumberGenerator.GetNext(),
                ObjectId = _owner.Id,
                PropertyName = _propertyName,
                ItemId = movedItem.Id,
                Index = e.NewStartingIndex
            };
            _operations.OnNext(operation);
        }
        else
        {
            var operation = new MoveCollectionRangeOperation
            {
                SequenceNumber = _sequenceNumberGenerator.GetNext(),
                ObjectId = _owner.Id,
                PropertyName = _propertyName,
                OldIndex = e.OldStartingIndex,
                NewIndex = e.NewStartingIndex,
                Count = e.OldItems.Count
            };
            _operations.OnNext(operation);
        }
    }

    private void EnqueueReplace(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            var operation = new RemoveCollectionRangeOperation
            {
                SequenceNumber = _sequenceNumberGenerator.GetNext(),
                ObjectId = _owner.Id,
                PropertyName = _propertyName,
                Index = e.OldStartingIndex,
                Count = e.OldItems.Count
            };
            _operations.OnNext(operation);
        }

        if (e.NewItems != null)
        {
            int index = e.NewStartingIndex;
            foreach (ICoreObject newItem in e.NewItems.OfType<ICoreObject>())
            {
                var json = CoreSerializerHelper.SerializeToJsonObject(newItem);
                var operation = new InsertCollectionItemOperation
                {
                    SequenceNumber = _sequenceNumberGenerator.GetNext(),
                    ObjectId = _owner.Id,
                    PropertyName = _propertyName,
                    Item = json,
                    Index = index++
                };
                _operations.OnNext(operation);
            }
        }
    }
}
