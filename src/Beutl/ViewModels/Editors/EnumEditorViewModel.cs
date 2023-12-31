using Avalonia;

using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class EnumEditorViewModel<T>(IAbstractProperty<T> property) : ValueEditorViewModel<T>(property)
    where T : struct, Enum
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EnumEditor<T> editor)
        {
            editor[!EnumEditor<T>.SelectedValueProperty] = Value.ToBinding();
            editor.ValueConfirmed += OnValueConfirmed;
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<T> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
