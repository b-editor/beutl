using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Subjects;

namespace Beutl.Protocol;

public class ListSynchronizer : ISynchronizer
{
    private readonly IList _list;
    private readonly ICoreObject _owner;
    private readonly string _propertyName;
    private readonly SequenceNumberGenerator _sequenceNumberGenerator;
    private readonly Subject<OperationBase> _operations = new();
    private readonly IDisposable _subscription;

    public ListSynchronizer(IObserver<OperationBase> observer, IList list, ICoreObject owner, string propertyName, SequenceNumberGenerator sequenceNumberGenerator)
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

    public IObservable<OperationBase> Operations => _operations;

    public void Dispose()
    {
        if (_list is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged -= OnCollectionChanged;
        }
        _subscription?.Dispose();
        _operations.OnCompleted();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 変更を転送する
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    // InsertObjectOperationを作成して_operationsにOnNextする
                    var index = e.NewStartingIndex;
                    foreach (ICoreObject newItem in e.NewItems.OfType<ICoreObject>())
                    {
                        var json = CoreSerializerHelper.SerializeToJsonObject(newItem);
                        var operation = new InsertObjectOperation
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
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    // RemoveRangeObjectOperationを作成して_operationsにOnNextする
                    var operation = new RemoveRangeObjectOperation
                    {
                        SequenceNumber = _sequenceNumberGenerator.GetNext(),
                        ObjectId = _owner.Id,
                        PropertyName = _propertyName,
                        Index = e.OldStartingIndex,
                        Count = e.OldItems.Count
                    };
                    _operations.OnNext(operation);
                }
                break;

            case NotifyCollectionChangedAction.Move:
                {
                    // MoveObjectOperationを作成して_operationsにOnNextする
                    if (e.OldItems != null && e.OldItems.Count == 1)
                    {
                        ICoreObject movedItem = (ICoreObject)e.OldItems[0]!;
                        var operation = new MoveObjectOperation
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
                        // 複数アイテムの移動はMoveRangeObjectOperationを使う
                        var operation = new MoveRangeObjectOperation
                        {
                            SequenceNumber = _sequenceNumberGenerator.GetNext(),
                            ObjectId = _owner.Id,
                            PropertyName = _propertyName,
                            OldIndex = e.OldStartingIndex,
                            NewIndex = e.NewStartingIndex,
                            Count = e.OldItems?.Count ?? 0
                        };
                        _operations.OnNext(operation);
                    }
                }
                break;
            case NotifyCollectionChangedAction.Replace:
                {
                    // ReplaceはRemoveRangeとAddの組み合わせとして扱う
                    if (e.OldItems != null)
                    {
                        var operation = new RemoveRangeObjectOperation
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
                        var index = e.NewStartingIndex;
                        foreach (ICoreObject newItem in e.NewItems.OfType<ICoreObject>())
                        {
                            var json = CoreSerializerHelper.SerializeToJsonObject(newItem);
                            var operation = new InsertObjectOperation
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
                break;
        }
    }
}
