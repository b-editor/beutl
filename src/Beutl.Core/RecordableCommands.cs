using System.Collections.Immutable;

using Beutl.Commands;

namespace Beutl;

public static partial class RecordableCommands
{
    public static IRecordableCommand Create(Action action, Action undo, ImmutableArray<IStorable?> storables, Action? redo = null)
    {
        return new DelegateCommand(
            action,
            undo,
            redo ?? action,
            storables);
    }

    public static RecordableCommandBuilder Create(ImmutableArray<IStorable?> storables)
    {
        return new RecordableCommandBuilder(storables);
    }
    
    public static RecordableCommandBuilder Create()
    {
        return new RecordableCommandBuilder([]);
    }

    public static IRecordableCommand Edit<T>(
        ICoreObject target,
        CoreProperty<T> property,
        T? value,
        Optional<T?> oldValue = default)
    {
        return new ChangePropertyCommand<T>(
            target,
            property,
            value,
            oldValue.HasValue ? oldValue.Value : target.GetValue(property),
            []);
    }

    private sealed class DelegateCommand(Action action, Action undo, Action redo, ImmutableArray<IStorable?> storables) : IRecordableCommand
    {
        public ImmutableArray<IStorable?> GetStorables() => storables;

        public void Do() => action();

        public void Redo() => redo();

        public void Undo() => undo();
    }
}
