using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;

namespace Beutl.ViewModels.Editors;

public sealed class StringEditorViewModel : ValueEditorViewModel<string>
{
    public StringEditorViewModel(IAbstractProperty<string> property)
        : base(property)
    {
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is StringEditor editor)
        {
            editor[!StringEditor.TextProperty] = Value.ToBinding();
            editor.ValueChanging += OnValueChanging;
            editor.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<string> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }

    private void OnValueChanging(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is StringEditor editor)
        {
            editor.Text = SetCurrentValueAndGetCoerced(editor.Text);
        }
    }
}
