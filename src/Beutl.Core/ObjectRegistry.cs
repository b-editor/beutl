using System.ComponentModel;
using System.Runtime;

namespace Beutl;

public class ObjectRegistry
{
    private readonly LinkedList<(Guid, CoreObject)> _objects = new();
    private readonly LinkedList<(Guid, DependentHandle)> _callbacks = new();
    private readonly object _lock = new();

    public static ObjectRegistry Current { get; } = new();

    public void Register(CoreObject obj)
    {
        lock (_lock)
        {
            _objects.AddLast((obj.Id, obj));
            SetResolved(obj.Id, obj);
            obj.PropertyChanged += OnObjectPropertyChanged;
        }
    }

    public void Unregister(CoreObject obj)
    {
        lock (_lock)
        {
            var node = _objects.First;
            while (node != null)
            {
                if (ReferenceEquals(node.Value.Item2, obj))
                {
                    obj.PropertyChanged -= OnObjectPropertyChanged;
                    _objects.Remove(node);
                    break;
                }

                node = node.Next;
            }
        }
    }

    public CoreObject? Find(Guid id)
    {
        lock (_lock)
        {
            var node = _objects.First;
            while (node != null)
            {
                if (node.Value.Item1 == id)
                {
                    return node.Value.Item2;
                }

                node = node.Next;
            }

            return null;
        }
    }

    public CoreObject[] Enumerate()
    {
        lock (_lock)
        {
            return [.._objects.Select(i => i.Item2)];
        }
    }

    public void Resolve<TSelf>(Guid id, TSelf self, Action<TSelf, CoreObject> callback)
        where TSelf : class
    {
        var obj = Find(id);
        if (obj != null)
        {
            callback(self, obj);
        }
        else
        {
            _callbacks.AddLast((id, new DependentHandle(self, new Action<object, CoreObject>((o, r) =>
            {
                callback((TSelf)o, r);
            }))));
        }
    }

    private void SetResolved(Guid id, CoreObject obj)
    {
        lock (_lock)
        {
            var node = _callbacks.First;
            while (node != null)
            {
                if (node.Value.Item1 == id)
                {
                    var (self, action) = node.Value.Item2.TargetAndDependent;
                    if (self != null && action != null)
                    {
                        ((Action<object, CoreObject>)action)(self, obj);
                    }

                    _callbacks.Remove(node);
                }

                node = node.Next;
            }
        }
    }

    private void OnObjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e is CorePropertyChangedEventArgs<Guid> args &&
            args.Property.Id == CoreObject.IdProperty.Id)
        {
            lock (_lock)
            {
                var node = _objects.First;
                while (node != null)
                {
                    if (ReferenceEquals(node.Value.Item2, args.Sender))
                    {
                        node.ValueRef = (args.NewValue, node.Value.Item2);
                        SetResolved(args.NewValue, node.Value.Item2);
                        break;
                    }

                    node = node.Next;
                }
            }
        }
    }
}
