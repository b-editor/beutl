using Avalonia;

using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class EnumEditorViewModel<T> : ValueEditorViewModel<T>
    where T : struct, Enum
{
    public EnumEditorViewModel(IAbstractProperty<T> property)
        : base(property)
    {
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EnumEditor<T> editor)
        {
            editor[!EnumEditor<T>.SelectedValueProperty] = Value.ToBinding();
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
}
