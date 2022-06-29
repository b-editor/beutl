namespace BeUtl.Commands;

public sealed class RemoveCommand<T> : IRecordableCommand
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

public sealed class RemoveAllCommand<T> : IRecordableCommand
{
    public RemoveAllCommand(IList<T> list, IReadOnlyList<T> items)
    {
        List = list;
        Items = items;
        Indices = new int[items.Count];

        for (int i = 0; i < items.Count; i++)
        {
            Indices[i] = list.IndexOf(items[i]);
        }

        Array.Sort(Indices);
    }

    public IList<T> List { get; }

    public IReadOnlyList<T> Items { get; }

    public int[] Indices { get; }

    public void Do()
    {
        for (int i = Indices.Length - 1; i >= 0; i--)
        {
            List.RemoveAt(Indices[i]);
        }
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        for (int i = 0; i < Indices.Length; i++)
        {
            List.Insert(Indices[i], Items[i]);
        }
    }
}
