using System.Numerics;

using Avalonia;

using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class NumberEditorViewModel<T> : ValueEditorViewModel<T>
    where T : struct, INumber<T>
{
    public NumberEditorViewModel(IAbstractProperty<T> property)
        : base(property)
    {
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is NumberEditor<T> editor)
        {
            editor[!NumberEditor<T>.ValueProperty] = Value.ToBinding();
            editor.ValueChanging += OnValueChanging;
            editor.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<T> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }

    private void OnValueChanging(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is NumberEditor<T> editor)
        {
            editor.Value = SetCurrentValueAndGetCoerced(editor.Value);
        }
    }
}
