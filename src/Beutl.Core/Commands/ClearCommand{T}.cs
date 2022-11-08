namespace Beutl.Commands;

internal sealed class ClearCommand<T> : IRecordableCommand
{
    public ClearCommand(IList<T> list)
    {
        List = list;
        Items = new T[list.Count];
        list.CopyTo(Items, 0);
    }

    public IList<T> List { get; }

    public T[] Items { get; }

    public void Do()
    {
        List.Clear();
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        foreach (T item in Items)
        {
            List.Add(item);
        }
    }
}
