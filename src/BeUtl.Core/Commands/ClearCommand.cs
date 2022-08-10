using System.Collections;

namespace BeUtl.Commands;

internal sealed class ClearCommand : IRecordableCommand
{
    public ClearCommand(IList list)
    {
        List = list;
        Items = new object[list.Count];
        list.CopyTo(Items, 0);
    }

    public IList List { get; }

    public object?[] Items { get; }

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
        foreach (object? item in Items)
        {
            List.Add(item);
        }
    }
}
