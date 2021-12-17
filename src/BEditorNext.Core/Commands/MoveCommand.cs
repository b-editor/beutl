using System.Collections;

namespace BEditorNext.Commands;

public sealed class MoveCommand : IRecordableCommand
{
    private readonly IList _list;
    private readonly int _newIndex;
    private readonly int _oldIndex;

    public MoveCommand(IList list, int newIndex, int oldIndex)
    {
        _list = list;
        _newIndex = newIndex;
        _oldIndex = oldIndex;
    }

    public void Do()
    {
        object? item = _list[_oldIndex];
        _list.RemoveAt(_oldIndex);
        _list.Insert(_newIndex, item);
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        object? item = _list[_newIndex];
        _list.RemoveAt(_newIndex);
        _list.Insert(_oldIndex, item);
    }
}
