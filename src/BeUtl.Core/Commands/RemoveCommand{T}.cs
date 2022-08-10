namespace BeUtl.Commands;

internal sealed class RemoveCommand<T> : IRecordableCommand
{
    public RemoveCommand(IList<T> list, T item)
    {
        List = list;
        Item = item;
        Index = list.IndexOf(Item);
    }

    public IList<T> List { get; }

    public T Item { get; }

    public int Index { get; private set; }

    public void Do()
    {
        Index = List.IndexOf(Item);
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
