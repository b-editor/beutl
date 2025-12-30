using Beutl.Engine;

namespace Beutl.Editor.Operations;

public sealed class InsertCollectionRangeOperation<T> : CollectionChangeOperation<T>, ICollectionChangeOperation
{
    public required T[] Items { get; set; }

    public required int Index { get; set; }

    IEnumerable<object?> ICollectionChangeOperation.Items => Items.Cast<object?>();

    protected override void ApplyToEngineProperty(IListProperty<T> listProperty)
    {
        int insertIndex = Index < 0 || Index > listProperty.Count ? listProperty.Count : Index;

        for (int i = 0; i < Items.Length; i++)
        {
            listProperty.Insert(insertIndex + i, Items[i]);
        }
    }

    protected override void ApplyTo(IList<T> list)
    {
        int insertIndex = Index < 0 || Index > list.Count ? list.Count : Index;

        for (int i = 0; i < Items.Length; i++)
        {
            list.Insert(insertIndex + i, Items[i]);
        }
    }

    protected override void RevertToEngineProperty(IListProperty<T> listProperty)
    {
        listProperty.RemoveRange(Index, Items.Length);
    }

    protected override void RevertTo(IList<T> list)
    {
        for (int i = 0; i < Items.Length; i++)
        {
            list.RemoveAt(Index);
        }
    }
}
