using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class RationalEditorViewModel(IAbstractProperty<Rational> property) : ValueEditorViewModel<Rational>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is RationalEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(RationalEditor.ValueProperty, Value.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<Rational> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is RationalEditor editor)
        {
            editor.Value = SetCurrentValueAndGetCoerced(editor.Value);
        }
    }
}
