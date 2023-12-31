using System.ComponentModel;

namespace Beutl;

public abstract class CorePropertyChangedEventArgs(
    CoreObject sender,
    CoreProperty property, 
    CorePropertyMetadata metadata) 
    : PropertyChangedEventArgs(property.Name)
{
    public CoreObject Sender { get; } = sender;

    public CoreProperty Property => GetProperty();

    public CorePropertyMetadata PropertyMetadata { get; } = metadata;

    public object? NewValue => GetNewValue();

    public object? OldValue => GetOldValue();

    protected abstract object? GetNewValue();

    protected abstract object? GetOldValue();

    protected abstract CoreProperty GetProperty();
}

public sealed class CorePropertyChangedEventArgs<TValue>(
    CoreObject sender,
    CoreProperty<TValue> property,
    CorePropertyMetadata metadata,
    TValue? newValue,
    TValue? oldValue)
    : CorePropertyChangedEventArgs(sender, property,metadata)
{
    public new CoreProperty<TValue> Property { get; } = property;

    public new TValue? NewValue { get; } = newValue;

    public new TValue? OldValue { get; } = oldValue;

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
