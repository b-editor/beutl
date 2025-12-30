using Beutl.Engine;

namespace Beutl.Editor.Operations;

public sealed class RemoveCollectionItemOperation<T> : CollectionChangeOperation<T>, ICollectionChangeOperation
{
    public required T Item { get; set; }

    public required int Index { get; set; }

    IEnumerable<object?> ICollectionChangeOperation.Items => [Item];

    protected override void ApplyToEngineProperty(IListProperty<T> listProperty)
    {
        listProperty.RemoveAt(Index);
    }

    protected override void ApplyTo(IList<T> list)
    {
        list.RemoveAt(Index);
    }

    protected override void RevertToEngineProperty(IListProperty<T> listProperty)
    {
        listProperty.Insert(Index, Item);
    }

    protected override void RevertTo(IList<T> list)
    {
        list.Insert(Index, Item);
    }
}
