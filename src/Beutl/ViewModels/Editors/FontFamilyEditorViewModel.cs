using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Media;

namespace Beutl.ViewModels.Editors;

public sealed class FontFamilyEditorViewModel : ValueEditorViewModel<FontFamily>
{
    public FontFamilyEditorViewModel(IAbstractProperty<FontFamily> property)
        : base(property)
    {
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is FontFamilyEditor editor)
        {
            editor[!FontFamilyEditor.ValueProperty] = Value.ToBinding();
            editor.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<FontFamily> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
