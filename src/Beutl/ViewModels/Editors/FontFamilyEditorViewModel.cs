using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Media;

namespace Beutl.ViewModels.Editors;

public sealed class FontFamilyEditorViewModel(IAbstractProperty<FontFamily?> property) : ValueEditorViewModel<FontFamily?>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is FontFamilyEditor editor)
        {
            editor[!FontFamilyEditor.ValueProperty] = Value.ToBinding();
            editor.ValueConfirmed += OnValueConfirmed;
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<FontFamily?> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
