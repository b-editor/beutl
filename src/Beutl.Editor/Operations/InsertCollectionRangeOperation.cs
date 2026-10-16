using Beutl.Engine;

namespace Beutl.Editor.Operations;

public sealed class InsertCollectionRangeOperation<T> : CollectionChangeOperation<T>, ICollectionChangeOperation
{
    public required T[] Items { get; set; }

    public required int Index { get; set; }

    IEnumerable<object?> ICollectionChangeOperation.Items => Items.Cast<object?>();

    protected override void ApplyTo(IList<T> list)
    {
        int insertIndex = GetInsertionIndex(list, Index);

        for (int i = 0; i < Items.Length; i++)
        {
            list.Insert(insertIndex + i, Items[i]);
        }
    }

    protected override void RevertToEngineProperty(IListProperty<T> listProperty)
    {
        RemoveItems(listProperty, Index, Items, listProperty.RemoveRange);
    }

    protected override void RevertTo(IList<T> list)
    {
        RemoveItems(list, Index, Items);
    }
}
