namespace BeUtl.Commands;

public sealed class ChangePropertyCommand : IRecordableCommand
{
    public ChangePropertyCommand(ICoreObject obj, CoreProperty property, object? newValue, object? oldValue)
    {
        Object = obj;
        Property = property;
        NewValue = newValue;
        OldValue = oldValue;
    }

    public ICoreObject Object { get; }

    public CoreProperty Property { get; }

    public object? NewValue { get; }

    public object? OldValue { get; }

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
