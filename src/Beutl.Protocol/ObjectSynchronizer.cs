using System.Collections;
using System.ComponentModel;
using System.Reactive.Subjects;

namespace Beutl.Protocol;

public class ObjectSynchronizer : ISynchronizer
{
    private readonly ICoreObject _object;
    private readonly Dictionary<int, ObjectSynchronizer> _childSynchronizers = new();
    private readonly Dictionary<int, ListSynchronizer> _childListSynchronizers = new();
    private readonly Subject<OperationBase> _operations = new();
    private readonly IDisposable? _subscription;
    public IObservable<OperationBase> Operations => _operations;

    public ObjectSynchronizer(
        IObserver<OperationBase> observer,
        ICoreObject obj,
        SequenceNumberGenerator sequenceNumberGenerator)
    {
        _object = obj;
        _subscription = _operations.Subscribe(observer);
        var props = PropertyRegistry.GetRegistered(obj.GetType());
        foreach (var prop in props)
        {
            var value = obj.GetValue(prop);
            if (value is IList list)
            {
                ListSynchronizer listSynchronizer = new(_operations, list, obj, prop.Name, sequenceNumberGenerator);
                _childListSynchronizers.Add(prop.Id, listSynchronizer);
            }
            else if (value is ICoreObject childObj)
            {
                ObjectSynchronizer childSynchronizer = new(_operations, childObj, sequenceNumberGenerator);
                _childSynchronizers.Add(prop.Id, childSynchronizer);
            }
        }
        obj.PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is CorePropertyChangedEventArgs args && sender is ICoreObject obj)
        {
            if (args.Property.Id == Hierarchical.HierarchicalParentProperty.Id) return;

            if (_childSynchronizers.Remove(args.Property.Id, out var childSynchronizer))
            {
                // 子オブジェクトの同期を停止
                childSynchronizer.Dispose();
            }

            // 新しい値がICoreObjectの場合、子オブジェクトの同期を開始
            if (args.NewValue is ICoreObject newChildObj)
            {
                ObjectSynchronizer newChildSynchronizer = new(_operations, newChildObj, new SequenceNumberGenerator());
                _childSynchronizers.Add(args.Property.Id, newChildSynchronizer);
            }

            Type type = typeof(UpdatePropertyOperation<>).MakeGenericType(args.Property.PropertyType);
            object operation = Activator.CreateInstance(type, obj.Id, args.Property.Name, args.NewValue)!;
            _operations.OnNext((OperationBase)operation);
        }
    }

    public void Dispose()
    {
        _object.PropertyChanged -= OnPropertyChanged;
        foreach (var child in _childSynchronizers.Values)
        {
            child.Dispose();
        }
        foreach (var childList in _childListSynchronizers.Values)
        {
            childList.Dispose();
        }
        _subscription?.Dispose();
        _operations.OnCompleted();
    }
}
