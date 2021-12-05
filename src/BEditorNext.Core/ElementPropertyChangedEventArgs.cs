using System.ComponentModel;

namespace BEditorNext;

public abstract class ElementPropertyChangedEventArgs : PropertyChangedEventArgs
{
    protected ElementPropertyChangedEventArgs(Element sender, PropertyDefine property)
        : base(property.Name)
    {
        Sender = sender;
    }

    public Element Sender { get; }

    public PropertyDefine Property => GetProperty();

    public object? NewValue => GetNewValue();

    public object? OldValue => GetOldValue();

    protected abstract object? GetNewValue();

    protected abstract object? GetOldValue();

    protected abstract PropertyDefine GetProperty();
}

public sealed class ElementPropertyChangedEventArgs<TValue> : ElementPropertyChangedEventArgs
{
    public ElementPropertyChangedEventArgs(Element sender, PropertyDefine<TValue> property, TValue? newValue, TValue? oldValue)
        : base(sender, property)
    {
        Property = property;
        NewValue = newValue;
        OldValue = oldValue;
    }

    public new PropertyDefine<TValue> Property { get; }

    public new TValue? NewValue { get; }

    public new TValue? OldValue { get; }

    protected override object? GetNewValue()
    {
        return NewValue;
    }

    protected override object? GetOldValue()
    {
        return OldValue;
    }

    protected override PropertyDefine GetProperty()
    {
        return Property;
    }
}
