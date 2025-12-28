using Beutl.Engine;

namespace Beutl.Editor.Operations;

public sealed class InsertCollectionItemOperation<T> : CollectionChangeOperation<T>, ICollectionChangeOperation
{
    public required T Item { get; set; }

    public required int Index { get; set; }

    IEnumerable<object?> ICollectionChangeOperation.Items => [Item];

    protected override void ApplyToEngineProperty(IListProperty<T> listProperty)
    {
        if (Index < 0 || Index > listProperty.Count)
        {
            listProperty.Add(Item);
        }
        else
        {
            listProperty.Insert(Index, Item);
        }
    }

    protected override void ApplyTo(IList<T> list)
    {
        if (Index < 0 || Index > list.Count)
        {
            list.Add(Item);
        }
        else
        {
            list.Insert(Index, Item);
        }
    }

    protected override void RevertToEngineProperty(IListProperty<T> listProperty)
    {
        listProperty.RemoveAt(Index);
    }

    protected override void RevertTo(IList<T> list)
    {
        list.RemoveAt(Index);
    }
}
