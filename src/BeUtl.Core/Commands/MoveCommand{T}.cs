using System.Collections.ObjectModel;

using BeUtl.Collections;

namespace BeUtl.Commands;

internal sealed class MoveCommand<T> : IRecordableCommand
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
        if (_list is ICoreList<T> coreList)
        {
            coreList.Move(_oldIndex, _newIndex);
        }
        else if (_list is ObservableCollection<T> observableCollection)
        {
            observableCollection.Move(_oldIndex, _newIndex);
        }
        else
        {
            T item = _list[_oldIndex];
            _list.RemoveAt(_oldIndex);
            _list.Insert(_newIndex, item);
        }
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        if (_list is ICoreList<T> coreList)
        {
            coreList.Move(_newIndex, _oldIndex);
        }
        else if (_list is ObservableCollection<T> observableCollection)
        {
            observableCollection.Move(_newIndex, _oldIndex);
        }
        else
        {
            T item = _list[_newIndex];
            _list.RemoveAt(_newIndex);
            _list.Insert(_oldIndex, item);
        }
    }
}
