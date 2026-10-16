using Beutl.Engine;

namespace Beutl.Editor.Operations;

public sealed class MoveCollectionRangeOperation<T> : CollectionChangeOperation<T>, ICollectionChangeOperation
{
    public required int OldIndex { get; set; }

    public required int NewIndex { get; set; }

    public required int Count { get; set; }

    // Lets replay find the items when an unrecorded change shifted them; empty skips that check.
    public T[] MovedItems { get; set; } = [];

    // The moved items stay in the list, so they are not reported as added or removed.
    IEnumerable<object?> ICollectionChangeOperation.Items => [];

    protected override void ApplyToEngineProperty(IListProperty<T> listProperty)
    {
        int oldIndex = FindBlock(listProperty, OldIndex, MovedItems);
        int newIndex = NewIndex;
        int count = Count;
        if (newIndex > oldIndex)
        {
            newIndex += count;
        }

        listProperty.MoveRange(oldIndex, count, newIndex);
    }

    protected override void ApplyTo(IList<T> list)
    {
        int oldIndex = FindBlock(list, OldIndex, MovedItems);
        var items = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = list[oldIndex]!;
            list.RemoveAt(oldIndex);
        }

        for (int i = 0; i < Count; i++)
        {
            list.Insert(NewIndex + i, items[i]);
        }
    }

    protected override void RevertToEngineProperty(IListProperty<T> listProperty)
    {
        int newIndex = FindBlock(listProperty, NewIndex, MovedItems);
        int oldIndex = OldIndex;
        int count = Count;
        if (oldIndex > newIndex)
        {
            oldIndex += count;
        }

        listProperty.MoveRange(newIndex, count, oldIndex);
    }

    protected override void RevertTo(IList<T> list)
    {
        int newIndex = FindBlock(list, NewIndex, MovedItems);
        var items = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = list[newIndex]!;
            list.RemoveAt(newIndex);
        }

        for (int i = 0; i < Count; i++)
        {
            list.Insert(OldIndex + i, items[i]);
        }
    }
}
