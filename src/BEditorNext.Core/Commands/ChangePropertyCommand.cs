namespace BEditorNext.Commands;

public sealed class ChangePropertyCommand : IRecordableCommand
{
    public ChangePropertyCommand(IElement element, PropertyDefine property, object? newValue, object? oldValue)
    {
        Element = element;
        Property = property;
        NewValue = newValue;
        OldValue = oldValue;
    }

    public ResourceReference<string> Name => "ChangePropertyString";

    public IElement Element { get; }

    public PropertyDefine Property { get; }

    public object? NewValue { get; }

    public object? OldValue { get; }

    public void Do()
    {
        Element.SetValue(Property, NewValue);
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        Element.SetValue(Property, OldValue);
    }
}
