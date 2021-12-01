using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace BEditorNext;

/// <summary>
/// List of <see cref="Element"/>.
/// </summary>
public interface IElementList : IObservableList<Element>
{
}

public interface IObservableList<T> : IList<T>, INotifyCollectionChanged
{
}

public class ObservableList<T> : ObservableCollection<T>, IObservableList<T>
{
}

internal sealed class ElementList : ObservableCollection<Element>, IElementList
{
    public ElementList(Element parent)
    {
        Parent = parent;
    }

    public Element Parent { get; }

    protected override void InsertItem(int index, Element item)
    {
        base.InsertItem(index, item);
        item.Parent = Parent;
    }

    protected override void RemoveItem(int index)
    {
        Element item = this[index];
        base.RemoveItem(index);
        item.Parent = null;
    }
}
