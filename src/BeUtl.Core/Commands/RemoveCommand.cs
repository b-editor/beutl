using System.Collections;

namespace Beutl.Commands;

internal sealed class RemoveCommand : IRecordableCommand
{
    public RemoveCommand(IList list, object? item)
    {
        List = list;
        Item = item;
        Index = list.IndexOf(Item);
    }

    public ResourceReference<string> Name => "S.Common.Remove";

    public IList List { get; }

    public object? Item { get; }

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
