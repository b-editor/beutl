using System.ComponentModel;

namespace Beutl;

public abstract class CorePropertyChangedEventArgs : PropertyChangedEventArgs
{
    protected CorePropertyChangedEventArgs(CoreObject sender, CoreProperty property, CorePropertyMetadata metadata)
        : base(property.Name)
    {
        Sender = sender;
        PropertyMetadata = metadata;
    }

    public CoreObject Sender { get; }

    public CoreProperty Property => GetProperty();
    
    public CorePropertyMetadata PropertyMetadata { get; }

    public object? NewValue => GetNewValue();

    public object? OldValue => GetOldValue();

    protected abstract object? GetNewValue();

    protected abstract object? GetOldValue();

    protected abstract CoreProperty GetProperty();
}

public sealed class CorePropertyChangedEventArgs<TValue> : CorePropertyChangedEventArgs
{
    public CorePropertyChangedEventArgs(CoreObject sender, CoreProperty<TValue> property, CorePropertyMetadata metadata, TValue? newValue, TValue? oldValue)
        : base(sender, property,metadata)
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
