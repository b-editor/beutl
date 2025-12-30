using Beutl.Engine;

namespace Beutl.Editor.Operations;

public sealed class RemoveCollectionRangeOperation<T> : CollectionChangeOperation<T>, ICollectionChangeOperation
{
    public required int Index { get; set; }

    public required T[] Items { get; set; }

    IEnumerable<object?> ICollectionChangeOperation.Items => Items.Cast<object?>();

    protected override void ApplyToEngineProperty(IListProperty<T> listProperty)
    {
        listProperty.RemoveRange(Index, Items.Length);
    }

    protected override void ApplyTo(IList<T> list)
    {
        for (int i = 0; i < Items.Length; i++)
        {
            list.RemoveAt(Index);
        }
    }

    protected override void RevertToEngineProperty(IListProperty<T> listProperty)
    {
        for (int i = 0; i < Items.Length; i++)
        {
            listProperty.Insert(Index + i, Items[i]);
        }
    }

    protected override void RevertTo(IList<T> list)
    {
        for (int i = 0; i < Items.Length; i++)
        {
            list.Insert(Index + i, Items[i]);
        }
    }
}
