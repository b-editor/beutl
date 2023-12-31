using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Media;

namespace Beutl.ViewModels.Editors;

public sealed class AlignmentXEditorViewModel(IAbstractProperty<AlignmentX> property) : ValueEditorViewModel<AlignmentX>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is AlignmentXEditor view)
        {
            view[!AlignmentXEditor.ValueProperty] = Value.ToBinding();
            view.ValueConfirmed += OnValueConfirmed;
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<AlignmentX> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
