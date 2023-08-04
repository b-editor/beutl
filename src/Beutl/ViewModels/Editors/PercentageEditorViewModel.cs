using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;

namespace Beutl.ViewModels.Editors;

public sealed class PercentageEditorViewModel : ValueEditorViewModel<float>
{
    public PercentageEditorViewModel(IAbstractProperty<float> property)
        : base(property)
    {
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is PercentageEditor editor)
        {
            editor[!PercentageEditor.ValueProperty] = Value.ToBinding();
            editor.ValueChanging += OnValueChanging;
            editor.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<float> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }

    private void OnValueChanging(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is PercentageEditor editor)
        {
            editor.Value = SetCurrentValueAndGetCoerced(editor.Value);
        }
    }
}
