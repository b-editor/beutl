using System.Collections;

using Beutl.Collections;

namespace Beutl.Commands;

internal sealed class MoveCommand(IList list, int newIndex, int oldIndex) : IRecordableCommand
{
    public void Do()
    {
        if (list is ICoreList coreList)
        {
            coreList.Move(oldIndex, newIndex);
        }
        else
        {
            object? item = list[oldIndex];
            list.RemoveAt(oldIndex);
            list.Insert(newIndex, item);
        }
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        if (list is ICoreList coreList)
        {
            coreList.Move(newIndex, oldIndex);
        }
        else
        {
            object? item = list[newIndex];
            list.RemoveAt(newIndex);
            list.Insert(oldIndex, item);
        }
    }
}
