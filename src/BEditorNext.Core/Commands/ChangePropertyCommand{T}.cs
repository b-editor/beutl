namespace BEditorNext.Commands;

public sealed class ChangePropertyCommand<T> : IRecordableCommand
{
    public ChangePropertyCommand(IElement element, PropertyDefine<T> property, T? newValue, T? oldValue)
    {
        Element = element;
        Property = property;
        NewValue = newValue;
        OldValue = oldValue;
    }

    public ResourceReference<string> Name => "ChangePropertyString";

    public IElement Element { get; }

    public PropertyDefine<T> Property { get; }

    public T? NewValue { get; }

    public T? OldValue { get; }

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
