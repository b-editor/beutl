using Beutl.Engine;

namespace Beutl.Editor.Operations;

public sealed class RemoveCollectionRangeOperation<T> : CollectionChangeOperation<T>, ICollectionChangeOperation
{
    public required int Index { get; set; }

    public required T[] Items { get; set; }

    IEnumerable<object?> ICollectionChangeOperation.Items => Items.Cast<object?>();

    protected override void ApplyToEngineProperty(IListProperty<T> listProperty)
    {
        RemoveItems(listProperty, Index, Items, listProperty.RemoveRange);
    }

    protected override void ApplyTo(IList<T> list)
    {
        RemoveItems(list, Index, Items);
    }

    protected override void RevertTo(IList<T> list)
    {
        int index = GetInsertionIndex(list, Index);
        for (int i = 0; i < Items.Length; i++)
        {
            list.Insert(index + i, Items[i]);
        }
    }
}
