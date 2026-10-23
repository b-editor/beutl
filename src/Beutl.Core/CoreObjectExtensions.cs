namespace Beutl;

public static class CoreObjectExtensions
{
    // The property's value now and then each new one.
    public static IObservable<T> GetObservable<T>(this IProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return new PropertyValueObservable<T>(property);
    }

    public static ICoreObject? FindById(this ICoreObject obj, Guid id, bool includeSelf = true)
    {
        return obj.Find(o => (o as ICoreObject)?.Id.Value == id, includeSelf) as ICoreObject;
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
}
