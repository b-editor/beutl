using Beutl.Engine;

namespace Beutl.Editor.Operations;

public sealed class MoveCollectionItemOperation<T> : CollectionChangeOperation<T>, ICollectionChangeOperation
{
    public required int OldIndex { get; set; }

    public required int NewIndex { get; set; }

    // Move操作ではアイテムを直接保持しないため、空のコレクションを返す
    IEnumerable<object?> ICollectionChangeOperation.Items => [];

    protected override void ApplyToEngineProperty(IListProperty<T> listProperty)
    {
        listProperty.Move(OldIndex, NewIndex);
    }

    protected override void ApplyTo(IList<T> list)
    {
        var item = list[OldIndex];
        list.RemoveAt(OldIndex);
        list.Insert(NewIndex, item);
    }

    protected override void RevertToEngineProperty(IListProperty<T> listProperty)
    {
        listProperty.Move(NewIndex, OldIndex);
    }

    protected override void RevertTo(IList<T> list)
    {
        var item = list[NewIndex];
        list.RemoveAt(NewIndex);
        list.Insert(OldIndex, item);
    }
}
