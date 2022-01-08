namespace BEditorNext.Commands;

public sealed class ChangePropertyCommand<T> : IRecordableCommand
{
    public ChangePropertyCommand(ICoreObject obj, CoreProperty<T> property, T? newValue, T? oldValue)
    {
        Object = obj;
        Property = property;
        NewValue = newValue;
        OldValue = oldValue;
    }

    public ResourceReference<string> Name => "ChangePropertyString";

    public ICoreObject Object { get; }

    public CoreProperty<T> Property { get; }

    public T? NewValue { get; }

    public T? OldValue { get; }

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
