using Beutl.Engine;

namespace Beutl.Editor.Operations;

public sealed class MoveCollectionRangeOperation<T> : CollectionChangeOperation<T>, ICollectionChangeOperation
{
    public required int OldIndex { get; set; }

    public required int NewIndex { get; set; }

    public required int Count { get; set; }

    // Move操作ではアイテムを直接保持しないため、空のコレクションを返す
    IEnumerable<object?> ICollectionChangeOperation.Items => [];

    protected override void ApplyToEngineProperty(IListProperty<T> listProperty)
    {
        int oldIndex = OldIndex;
        int newIndex = NewIndex;
        int count = Count;
        if (newIndex > oldIndex)
        {
            newIndex += count;
        }

        listProperty.MoveRange(OldIndex, Count, newIndex);
    }

    protected override void ApplyTo(IList<T> list)
    {
        var items = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = list[OldIndex]!;
            list.RemoveAt(OldIndex);
        }

        for (int i = 0; i < Count; i++)
        {
            list.Insert(NewIndex + i, items[i]);
        }
    }

    protected override void RevertToEngineProperty(IListProperty<T> listProperty)
    {
        int oldIndex = OldIndex;
        int newIndex = NewIndex;
        int count = Count;
        if (oldIndex > newIndex)
        {
            oldIndex += count;
        }

        listProperty.MoveRange(NewIndex, Count, oldIndex);
    }

    protected override void RevertTo(IList<T> list)
    {
        var items = new T[Count];
        for (int i = 0; i < Count; i++)
        {
            items[i] = list[NewIndex]!;
            list.RemoveAt(NewIndex);
        }

        for (int i = 0; i < Count; i++)
        {
            list.Insert(OldIndex + i, items[i]);
        }
    }
}
