using System.ComponentModel;

using Beutl.Reactive;

namespace Beutl;

public static class CoreObjectExtensions
{
    public static IObservable<CorePropertyChangedEventArgs<T>> GetPropertyChangedObservable<T>(this ICoreObject obj, CoreProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(property);

        return new CorePropertyChangedObservable<T>(obj, property);
    }

    public static IObservable<T> GetObservable<T>(this ICoreObject obj, CoreProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(property);

        return new CoreObjectSubject<T>(obj, property);
    }

    public static ICoreObject? FindById(this ICoreObject obj, Guid id, bool includeSelf = true)
    {
        return obj.Find(o => (o as ICoreObject)?.Id == id, includeSelf) as ICoreObject;
    }

    public static object? Find(this ICoreObject obj, Predicate<object?> predicate, bool includeSelf = true)
    {
        return obj.Find(predicate, includeSelf, []);
    }

    public static object? Find(this ICoreObject obj, Predicate<object?> predicate, bool includeSelf, HashSet<object> hashSet)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (!hashSet.Add(obj))
            return null;

        if (includeSelf && predicate(obj))
            return obj;

        if (obj is IHierarchical hierarchical)
        {
            object? match = hierarchical.FindHierarchy(predicate, hashSet);
            if (match != null)
                return match;
        }

        Type type = obj.GetType();

        IReadOnlyList<CoreProperty> props = PropertyRegistry.GetRegistered(type);
        foreach (CoreProperty prop in props)
        {
            object? inner = obj.GetValue(prop);
            if (inner != null && hashSet.Add(inner))
            {
                if (predicate(inner))
                {
                    return inner;
                }
                else if ((inner as ICoreObject)?.Find(predicate, true, hashSet) is { } match)
                {
                    return match;
                }
            }
        }

        return null;
    }

    private static object? FindHierarchy(this IHierarchical hierarchical, Predicate<object?> predicate, HashSet<object> hashSet)
    {
        foreach (IHierarchical item in hierarchical.HierarchicalChildren)
        {
            if (hashSet.Add(item))
            {
                if (predicate(item))
                {
                    return item;
                }
                else if ((item as ICoreObject)?.Find(predicate, true, hashSet) is { } match)
                {
                    return match;
                }
            }
        }

        return null;
    }

    private sealed class CorePropertyChangedObservable<T>(ICoreObject o, CoreProperty<T> property)
        : LightweightObservableBase<CorePropertyChangedEventArgs<T>>
    {
        private void Object_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e is CorePropertyChangedEventArgs<T> a && a.Property == property)
            {
                PublishNext(a);
            }
        }

        protected override void Deinitialize()
        {
            o.PropertyChanged -= Object_PropertyChanged;
        }

        protected override void Initialize()
        {
            o.PropertyChanged += Object_PropertyChanged;
        }
    }
}
