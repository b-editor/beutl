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

    // Without the items, stale indexes fail as before instead of moving something else.
    private int GetDestination(IList<T> list, int index)
        => MovedItems.Length == 0 ? index : GetMoveDestination(list, index, Count);

    protected override void ApplyToEngineProperty(IListProperty<T> listProperty)
    {
        int oldIndex = FindBlock(listProperty, OldIndex, MovedItems);
        int newIndex = GetDestination(listProperty, NewIndex);
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
        int newIndex = GetDestination(list, NewIndex);
        var items = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = list[oldIndex]!;
            list.RemoveAt(oldIndex);
        }

        for (int i = 0; i < Count; i++)
        {
            list.Insert(newIndex + i, items[i]);
        }
    }

    protected override void RevertToEngineProperty(IListProperty<T> listProperty)
    {
        int newIndex = FindBlock(listProperty, NewIndex, MovedItems);
        int oldIndex = GetDestination(listProperty, OldIndex);
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
        int oldIndex = GetDestination(list, OldIndex);
        var items = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = list[newIndex]!;
            list.RemoveAt(newIndex);
        }

        for (int i = 0; i < Count; i++)
        {
            list.Insert(oldIndex + i, items[i]);
        }
    }
}
