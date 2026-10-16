using System.Diagnostics;
using Avalonia.Interactivity;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Controls.PropertyEditors;

public abstract class PropertyEditorValueChangedEventArgs : RoutedEventArgs
{
    protected PropertyEditorValueChangedEventArgs(RoutedEvent routedEvent)
        : base(routedEvent)
    {
    }

    public object? NewValue => GetNewValue();

    public PropertyEditorValueChangedEventArgs<TValue>? Expect<TValue>(object? sender)
    {
        var args = this as PropertyEditorValueChangedEventArgs<TValue>;
        if (args is null)
            ReportMismatch<TValue>(sender);

        return args;
    }

    protected abstract object? GetNewValue();

    // A nested editor's event bubbles up with its own type, so only an event from the handler's own editor is a mismatch.
    [Conditional("DEBUG")]
    private void ReportMismatch<TValue>(object? sender)
    {
        if (sender is null || !ReferenceEquals(sender, Source))
            return;

        string message = $"{sender.GetType().Name} raised {RoutedEvent?.Name} as {GetType()}, but its handler expects {typeof(PropertyEditorValueChangedEventArgs<TValue>)}.";
        Log.CreateLogger<PropertyEditorValueChangedEventArgs>().LogError("{Message}", message);
        Debug.Fail(message);
    }
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
