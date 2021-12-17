namespace BEditorNext.Commands;

public sealed class MoveCommand<T> : IRecordableCommand
{
    private readonly IList<T> _list;
    private readonly int _newIndex;
    private readonly int _oldIndex;

    public MoveCommand(IList<T> list, int newIndex, int oldIndex)
    {
        _list = list;
        _newIndex = newIndex;
        _oldIndex = oldIndex;
    }

    public void Do()
    {
        T item = _list[_oldIndex];
        _list.RemoveAt(_oldIndex);
        _list.Insert(_newIndex, item);
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        T item = _list[_newIndex];
        _list.RemoveAt(_newIndex);
        _list.Insert(_oldIndex, item);
    }
}
