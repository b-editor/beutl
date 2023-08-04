using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Media;

namespace Beutl.ViewModels.Editors;

public sealed class AlignmentYEditorViewModel : ValueEditorViewModel<AlignmentY>
{
    public AlignmentYEditorViewModel(IAbstractProperty<AlignmentY> property)
        : base(property)
    {
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is AlignmentYEditor view)
        {
            view[!AlignmentYEditor.ValueProperty] = Value.ToBinding();
            view.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<AlignmentY> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
