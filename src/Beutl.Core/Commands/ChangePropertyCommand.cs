namespace Beutl.Commands;

public sealed class ChangePropertyCommand(ICoreObject obj, CoreProperty property, object? newValue, object? oldValue) : IRecordableCommand
{
    public ICoreObject Object { get; } = obj;

    public CoreProperty Property { get; } = property;

    public object? NewValue { get; } = newValue;

    public object? OldValue { get; } = oldValue;

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
