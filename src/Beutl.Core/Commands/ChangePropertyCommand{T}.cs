namespace Beutl.Commands;

public sealed class ChangePropertyCommand<T>(ICoreObject obj, CoreProperty<T> property, T? newValue, T? oldValue) : IRecordableCommand
{
    public ICoreObject Object { get; } = obj;

    public CoreProperty<T> Property { get; } = property;

    public T? NewValue { get; } = newValue;

    public T? OldValue { get; } = oldValue;

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
