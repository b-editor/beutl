namespace Beutl.Commands;

internal sealed class RemoveCommand<T> : IRecordableCommand
{
    public RemoveCommand(IList<T> list, T item)
    {
        List = list;
        Item = item;
        Index = list.IndexOf(Item);
    }

    public RemoveCommand(IList<T> list, int index)
    {
        List = list;
        Index = index;
        Item = list[index];
    }

    public IList<T> List { get; }

    public T Item { get; }

    public int Index { get; private set; }

    public void Do()
    {
        List.RemoveAt(Index);
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        List.Insert(Index, Item);
    }
}
