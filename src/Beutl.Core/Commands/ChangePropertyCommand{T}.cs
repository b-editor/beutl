using System.Collections.Immutable;

namespace Beutl.Commands;

internal sealed class ChangePropertyCommand<T>(
    ICoreObject obj,
    CoreProperty<T> property,
    T? newValue,
    T? oldValue,
    ImmutableArray<IStorable?> storables) : IRecordableCommand
{
    public ICoreObject Object { get; } = obj;

    public CoreProperty<T> Property { get; } = property;

    public T? NewValue { get; } = newValue;

    public T? OldValue { get; } = oldValue;

    public ImmutableArray<IStorable?> GetStorables() => storables;

    public void Do()
    {
        Object.SetValue(Property, NewValue);
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        Object.SetValue(Property, OldValue);
    }
}
