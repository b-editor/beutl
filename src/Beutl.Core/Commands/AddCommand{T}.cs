namespace Beutl.Commands;

internal sealed class AddCommand<T>(IList<T> list, T item, int index) : IRecordableCommand
{
    public IList<T> List { get; } = list;

    public T Item { get; } = item;

    public int Index { get; } = index;

    public void Do()
    {
        List.Insert(Index, Item);
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        List.Remove(Item);
    }
}
