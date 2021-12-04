using System.Reactive.Subjects;

namespace BEditorNext;

public static class ElementExtensions
{
    public static Element? Find(this Element self, Guid id)
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

    public static Element? FindAllChildren(this Element self, Guid id)
    {
        ArgumentNullException.ThrowIfNull(self);

        foreach (Element item in self.EnumerateAllChildren<Element>())
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    public static IObservable<T> GetObservable<T>(this Element obj, PropertyDefine<T> property)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(property);

        return new ElementSubject<T>(obj, property);
    }

    public static ISubject<T> GetSubject<T>(this Element obj, PropertyDefine<T> property)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(property);

        return new ElementSubject<T>(obj, property);
    }
}
