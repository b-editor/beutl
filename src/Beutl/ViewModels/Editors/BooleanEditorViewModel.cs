using Avalonia;

using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class BooleanEditorViewModel(IAbstractProperty<bool> property) : ValueEditorViewModel<bool>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is BooleanEditor view)
        {
            view[!BooleanEditor.ValueProperty] = Value.ToBinding();
            view.ValueConfirmed += OnValueConfirmed;
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<bool> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
