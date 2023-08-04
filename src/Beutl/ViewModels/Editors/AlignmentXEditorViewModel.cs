using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Media;

namespace Beutl.ViewModels.Editors;

public sealed class AlignmentXEditorViewModel : ValueEditorViewModel<AlignmentX>
{
    public AlignmentXEditorViewModel(IAbstractProperty<AlignmentX> property)
        : base(property)
    {
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is AlignmentXEditor view)
        {
            view[!AlignmentXEditor.ValueProperty] = Value.ToBinding();
            view.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<AlignmentX> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
