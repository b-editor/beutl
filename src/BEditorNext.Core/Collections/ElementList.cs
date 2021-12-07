using System.Collections.ObjectModel;

namespace BEditorNext.Collections;

internal sealed class ElementList : ObservableCollection<Element>, IElementList
{
    public ElementList(Element parent)
    {
        Parent = parent;
    }

    public Element Parent { get; }

    protected override void InsertItem(int index, Element item)
    {
        item.RaiseParentChanging(item.Parent, Parent);
        item.SetParent(Parent);
        base.InsertItem(index, item);
        item.RaiseParentChanged();
    }

    protected override void RemoveItem(int index)
    {
        Element item = this[index];
        base.RemoveItem(index);
        item.Parent = null;
    }
}
