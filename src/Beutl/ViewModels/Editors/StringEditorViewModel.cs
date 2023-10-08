using System.ComponentModel.DataAnnotations;

using Avalonia;

using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class StringEditorViewModel : ValueEditorViewModel<string?>
{
    public StringEditorViewModel(IAbstractProperty<string?> property)
        : base(property)
    {
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is StringEditor editor)
        {
            CoreProperty? prop = WrappedProperty.GetCoreProperty();
            bool multiline = false;
            if (prop != null)
            {
                CorePropertyMetadata metadata = prop.GetMetadata<CorePropertyMetadata>(WrappedProperty.ImplementedType);
                multiline = metadata.Attributes.Any(v => v is DataTypeAttribute { DataType: DataType.MultilineText });
            }

            editor[!StringEditor.TextProperty] = Value.ToBinding();
            editor.ValueChanged += OnValueChanged;
            editor.ValueConfirmed += OnValueConfirmed;
            editor.Classes.Set("multiline", multiline);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<string?> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is StringEditor editor)
        {
            editor.Text = SetCurrentValueAndGetCoerced(editor.Text);
        }
    }
}
