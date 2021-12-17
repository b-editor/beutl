namespace BEditorNext.Commands;

public sealed class AddCommand<T> : IRecordableCommand
{
    public AddCommand(IList<T> list, T item, int index)
    {
        List = list;
        Item = item;
        Index = index;
    }

    public ResourceReference<string> Name => "AddItemString";

    public IList<T> List { get; }

    public T Item { get; }

    public int Index { get; }

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
