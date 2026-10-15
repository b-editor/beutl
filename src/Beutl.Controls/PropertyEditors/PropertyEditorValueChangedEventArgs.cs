using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

public abstract class PropertyEditorValueChangedEventArgs : RoutedEventArgs
{
    protected PropertyEditorValueChangedEventArgs(RoutedEvent routedEvent)
        : base(routedEvent)
    {
    }

    public object? NewValue => GetNewValue();

    protected abstract object? GetNewValue();
}

public class PropertyEditorValueChangedEventArgs<TValue> : PropertyEditorValueChangedEventArgs
{
    public PropertyEditorValueChangedEventArgs(TValue newValue, TValue oldValue, RoutedEvent routedEvent)
        : base(routedEvent)
    {
        NewValue = newValue;
        OldValue = oldValue;
    }

    public new TValue NewValue { get; }

    public TValue OldValue { get; }

    protected override object? GetNewValue() => NewValue;
}
