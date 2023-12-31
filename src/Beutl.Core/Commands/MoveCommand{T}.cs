using System.Collections.ObjectModel;

using Beutl.Collections;

namespace Beutl.Commands;

internal sealed class MoveCommand<T>(IList<T> list, int newIndex, int oldIndex) : IRecordableCommand
{
    public void Do()
    {
        if (list is ICoreList<T> coreList)
        {
            coreList.Move(oldIndex, newIndex);
        }
        else if (list is ObservableCollection<T> observableCollection)
        {
            observableCollection.Move(oldIndex, newIndex);
        }
        else
        {
            T item = list[oldIndex];
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
        if (list is ICoreList<T> coreList)
        {
            coreList.Move(newIndex, oldIndex);
        }
        else if (list is ObservableCollection<T> observableCollection)
        {
            observableCollection.Move(newIndex, oldIndex);
        }
        else
        {
            T item = list[newIndex];
            list.RemoveAt(newIndex);
            list.Insert(oldIndex, item);
        }
    }
}
