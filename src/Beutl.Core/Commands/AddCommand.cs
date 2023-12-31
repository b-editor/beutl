using System.Collections;

namespace Beutl.Commands;

internal sealed class AddCommand(IList list, object? item, int index) : IRecordableCommand
{
    public IList List { get; } = list;

    public object? Item { get; } = item;

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
