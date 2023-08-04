using Avalonia;

using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class BooleanEditorViewModel : ValueEditorViewModel<bool>
{
    public BooleanEditorViewModel(IAbstractProperty<bool> property)
        : base(property)
    {
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is BooleanEditor view)
        {
            view[!BooleanEditor.ValueProperty] = Value.ToBinding();
            view.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<bool> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
