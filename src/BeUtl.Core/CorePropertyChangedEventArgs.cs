using System.ComponentModel;

namespace BeUtl;

public abstract class ElementPropertyChangedEventArgs : PropertyChangedEventArgs
{
    protected ElementPropertyChangedEventArgs(CoreObject sender, CoreProperty property)
        : base(property.Name)
    {
        Sender = sender;
    }

    public CoreObject Sender { get; }

    public CoreProperty Property => GetProperty();

    public object? NewValue => GetNewValue();

    public object? OldValue => GetOldValue();

    protected abstract object? GetNewValue();

    protected abstract object? GetOldValue();

    protected abstract CoreProperty GetProperty();
}

public sealed class CorePropertyChangedEventArgs<TValue> : ElementPropertyChangedEventArgs
{
    public CorePropertyChangedEventArgs(CoreObject sender, CoreProperty<TValue> property, TValue? newValue, TValue? oldValue)
        : base(sender, property)
    {
        Property = property;
        NewValue = newValue;
        OldValue = oldValue;
    }

    public new CoreProperty<TValue> Property { get; }

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

    protected override CoreProperty GetProperty()
    {
        return Property;
    }
}
