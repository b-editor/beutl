using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class TimeSpanEditorViewModel(IPropertyAdapter<TimeSpan> property) : ValueEditorViewModel<TimeSpan>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is TimeSpanEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(TimeSpanEditor.ValueProperty, Value.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<TimeSpan> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is TimeSpanEditor editor)
        {
            editor.Value = SetCurrentValueAndGetCoerced(editor.Value);
        }
    }
}
