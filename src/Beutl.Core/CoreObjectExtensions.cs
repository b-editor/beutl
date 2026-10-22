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

    // The property's value now and then each new one, as GetObservable gives a CoreProperty's.
    public static IObservable<T> GetObservable<T>(this IProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return new PropertyValueObservable<T>(property);
    }

    public static ICoreObject? FindById(this ICoreObject obj, Guid id, bool includeSelf = true)
    {
        return obj.Find(o => (o as ICoreObject)?.Id == id, includeSelf) as ICoreObject;
    }

    public static object? Find(this ICoreObject obj, Predicate<object?> predicate, bool includeSelf = true)
    {
        return obj.Find(predicate, includeSelf, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    public static object? Find(this ICoreObject obj, Predicate<object?> predicate, bool includeSelf, HashSet<object> hashSet)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (!hashSet.Add(obj))
            return null;

        if (includeSelf && predicate(obj))
            return obj;

        return FindBelow(obj, predicate, hashSet);
    }

    // The first match below obj, which stays below it rather than searching through its parent.
    private static object? FindBelow(object obj, Predicate<object?> predicate, HashSet<object> hashSet)
    {
        foreach (object child in ChildObjects.Of(obj))
        {
            if (!child.GetType().IsValueType && !hashSet.Add(child))
                continue;

            if (predicate(child))
                return child;

            if (FindBelow(child, predicate, hashSet) is { } match)
                return match;
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
