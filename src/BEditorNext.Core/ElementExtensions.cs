using System.Reactive.Subjects;

namespace BEditorNext;

public static class ElementExtensions
{
    public static IElement? Find(this IElement self, Guid id)
    {
        ArgumentNullException.ThrowIfNull(self);

        foreach (Element item in self.Children)
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    public static IElement? FindAllChildren(this IElement self, Guid id)
    {
        ArgumentNullException.ThrowIfNull(self);

        foreach (IElement item in self.EnumerateAllChildren<IElement>())
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
