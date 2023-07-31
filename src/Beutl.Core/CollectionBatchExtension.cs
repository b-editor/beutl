using System.Collections;

using Beutl.Commands;

namespace Beutl;

public static class CollectionBatchExtension
{
    public static ICollectionBatchChanges<T> BeginRecord<T>(this IList<T> list)
    {
        return new CollectionBatchChanges<T>(list);
    }

    public static ICollectionBatchChanges BeginRecord(this IList list)
    {
        return new CollectionBatchChanges(list);
    }

    public interface ICollectionBatchChanges
    {
        ICollectionBatchChanges Add(object? item);

        ICollectionBatchChanges Insert(int index, object? item);

        ICollectionBatchChanges Move(int oldIndex, int newIndex);

        ICollectionBatchChanges Remove(object? item);
        
        ICollectionBatchChanges RemoveAt(int index);

        ICollectionBatchChanges Clear();

        IRecordableCommand ToCommand();
    }

    public interface ICollectionBatchChanges<T>
    {
        ICollectionBatchChanges<T> Add(T item);

        ICollectionBatchChanges<T> Insert(int index, T item);

        ICollectionBatchChanges<T> Move(int oldIndex, int newIndex);

        ICollectionBatchChanges<T> Remove(T item);

        ICollectionBatchChanges<T> RemoveAt(int index);

        ICollectionBatchChanges<T> Clear();

        IRecordableCommand ToCommand();
    }

    private sealed class CollectionBatchChanges : ICollectionBatchChanges
    {
        private readonly List<IRecordableCommand> _commands = new();
        private readonly IList _list;

        public CollectionBatchChanges(IList list)
        {
            _list = list;
        }

        public ICollectionBatchChanges Add(object? item)
        {
            _commands.Add(new AddCommand(_list, item, _list.Count));
            return this;
        }

        public ICollectionBatchChanges Clear()
        {
            _commands.Add(new ClearCommand(_list));
            return this;
        }

        public ICollectionBatchChanges Insert(int index, object? item)
        {
            _commands.Add(new AddCommand(_list, item, index));
            return this;
        }

        public ICollectionBatchChanges Move(int oldIndex, int newIndex)
        {
            _commands.Add(new MoveCommand(_list, newIndex, oldIndex));
            return this;
        }

        public ICollectionBatchChanges Remove(object? item)
        {
            _commands.Add(new RemoveCommand(_list, item));
            return this;
        }

        public ICollectionBatchChanges RemoveAt(int index)
        {
            _commands.Add(new RemoveCommand(_list, index));
            return this;
        }

        public IRecordableCommand ToCommand()
        {
            return new Command(_commands.ToArray());
        }
    }

    private sealed class CollectionBatchChanges<T> : ICollectionBatchChanges<T>
    {
        private readonly List<IRecordableCommand> _commands = new();
        private readonly IList<T> _list;

        public CollectionBatchChanges(IList<T> list)
        {
            _list = list;
        }

        public ICollectionBatchChanges<T> Add(T item)
        {
            _commands.Add(new AddCommand<T>(_list, item, _list.Count));
            return this;
        }

        public ICollectionBatchChanges<T> Clear()
        {
            _commands.Add(new ClearCommand<T>(_list));
            return this;
        }

        public ICollectionBatchChanges<T> Insert(int index, T item)
        {
            _commands.Add(new AddCommand<T>(_list, item, index));
            return this;
        }

        public ICollectionBatchChanges<T> Move(int oldIndex, int newIndex)
        {
            _commands.Add(new MoveCommand<T>(_list, newIndex, oldIndex));
            return this;
        }

        public ICollectionBatchChanges<T> Remove(T item)
        {
            _commands.Add(new RemoveCommand<T>(_list, item));
            return this;
        }

        public ICollectionBatchChanges<T> RemoveAt(int index)
        {
            _commands.Add(new RemoveCommand<T>(_list, index));
            return this;
        }

        public IRecordableCommand ToCommand()
        {
            return new Command(_commands.ToArray());
        }
    }

    private sealed class Command : IRecordableCommand
    {
        private readonly IRecordableCommand[] _commands;

        public Command(IRecordableCommand[] commands)
        {
            _commands = commands;
        }

        public void Do()
        {
            for (int i = 0; i < _commands.Length; i++)
            {
                _commands[i].Do();
            }
        }

        public void Redo()
        {
            for (int i = 0; i < _commands.Length; i++)
            {
                _commands[i].Redo();
            }
        }

        public void Undo()
        {
            for (int i = _commands.Length - 1; i >= 0; i--)
            {
                _commands[i].Undo();
            }
        }
    }
}
