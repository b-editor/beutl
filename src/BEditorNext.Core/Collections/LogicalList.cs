using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace BEditorNext.Collections;

public class LogicalList<T> : ObservableCollection<T>, IObservableList<T>
    where T : ILogicalElement
{
    public LogicalList(ILogicalElement parent)
    {
        Parent = parent;
    }

    public ILogicalElement Parent { get; }

    protected override void RemoveItem(int index)
    {
        T item = this[index];
        base.RemoveItem(index);

        item.NotifyDetachedFromLogicalTree(new LogicalTreeAttachmentEventArgs(Parent, null));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (ILogicalElement? item in e.NewItems.OfType<ILogicalElement>())
            {
                ILogicalElement? oldParent = item.LogicalParent;
                ILogicalElement? newParent = Parent;

                item.NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(oldParent, newParent));
            }
        }

        base.OnCollectionChanged(e);
    }
}
