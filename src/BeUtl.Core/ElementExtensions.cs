using System.Reactive.Subjects;

namespace BeUtl;

public static class ElementExtensions
{
    public static ICoreObject? Find(this IElement self, Guid id)
    {
        ArgumentNullException.ThrowIfNull(self);

        foreach (ICoreObject item in self.LogicalChildren)
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    public static ICoreObject? FindAllChildren(this IElement self, Guid id)
    {
        ArgumentNullException.ThrowIfNull(self);

        foreach (ICoreObject item in self.EnumerateAllChildren<ICoreObject>())
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    public static IObservable<T> GetObservable<T>(this ICoreObject obj, CoreProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(property);

        return new CoreObjectSubject<T>(obj, property);
    }

    public static ISubject<T> GetSubject<T>(this ICoreObject obj, CoreProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(property);

        return new CoreObjectSubject<T>(obj, property);
    }
}
