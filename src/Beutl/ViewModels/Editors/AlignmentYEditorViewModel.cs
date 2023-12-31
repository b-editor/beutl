using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Media;

namespace Beutl.ViewModels.Editors;

public sealed class AlignmentYEditorViewModel(IAbstractProperty<AlignmentY> property) : ValueEditorViewModel<AlignmentY>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is AlignmentYEditor view)
        {
            view[!AlignmentYEditor.ValueProperty] = Value.ToBinding();
            view.ValueConfirmed += OnValueConfirmed;
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<AlignmentY> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
