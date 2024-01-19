using System.Collections;
using System.Collections.Immutable;

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

        IRecordableCommand ToCommand(ImmutableArray<IStorable?> storables);
    }

    public interface ICollectionBatchChanges<T>
    {
        ICollectionBatchChanges<T> Add(T item);

        ICollectionBatchChanges<T> Insert(int index, T item);

        ICollectionBatchChanges<T> Move(int oldIndex, int newIndex);

        ICollectionBatchChanges<T> Remove(T item);

        ICollectionBatchChanges<T> RemoveAt(int index);

        ICollectionBatchChanges<T> Clear();

        IRecordableCommand ToCommand(ImmutableArray<IStorable?> storables);
    }

    private sealed class CollectionBatchChanges(IList list) : ICollectionBatchChanges
    {
        private readonly List<IRecordableCommand> _commands = [];

        public ICollectionBatchChanges Add(object? item)
        {
            _commands.Add(new AddCommand(list, item, list.Count));
            return this;
        }

        public ICollectionBatchChanges Clear()
        {
            _commands.Add(new ClearCommand(list));
            return this;
        }

        public ICollectionBatchChanges Insert(int index, object? item)
        {
            _commands.Add(new AddCommand(list, item, index));
            return this;
        }

        public ICollectionBatchChanges Move(int oldIndex, int newIndex)
        {
            _commands.Add(new MoveCommand(list, newIndex, oldIndex));
            return this;
        }

        public ICollectionBatchChanges Remove(object? item)
        {
            _commands.Add(new RemoveCommand(list, item));
            return this;
        }

        public ICollectionBatchChanges RemoveAt(int index)
        {
            _commands.Add(new RemoveCommand(list, index));
            return this;
        }

        public IRecordableCommand ToCommand(ImmutableArray<IStorable?> storables)
        {
            return _commands.ToArray().ToCommand(storables);
        }
    }

    private sealed class CollectionBatchChanges<T>(IList<T> list) : ICollectionBatchChanges<T>
    {
        private readonly List<IRecordableCommand> _commands = [];

        public ICollectionBatchChanges<T> Add(T item)
        {
            _commands.Add(new AddCommand<T>(list, item, list.Count));
            return this;
        }

        public ICollectionBatchChanges<T> Clear()
        {
            _commands.Add(new ClearCommand<T>(list));
            return this;
        }

        public ICollectionBatchChanges<T> Insert(int index, T item)
        {
            _commands.Add(new AddCommand<T>(list, item, index));
            return this;
        }

        public ICollectionBatchChanges<T> Move(int oldIndex, int newIndex)
        {
            _commands.Add(new MoveCommand<T>(list, newIndex, oldIndex));
            return this;
        }

        public ICollectionBatchChanges<T> Remove(T item)
        {
            _commands.Add(new RemoveCommand<T>(list, item));
            return this;
        }

        public ICollectionBatchChanges<T> RemoveAt(int index)
        {
            _commands.Add(new RemoveCommand<T>(list, index));
            return this;
        }

        public IRecordableCommand ToCommand(ImmutableArray<IStorable?> storables)
        {
            return _commands.ToArray().ToCommand(storables);
        }
    }
}
