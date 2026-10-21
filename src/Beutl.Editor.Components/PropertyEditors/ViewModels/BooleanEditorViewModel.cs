using Beutl.Controls.PropertyEditors;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class BooleanEditorViewModel(IPropertyAdapter<bool> property) : ValueEditorViewModel<bool>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is BooleanEditor view && !Disposables.IsDisposed)
        {
            AttachConfirmedValueBinding(view, BooleanEditor.ValueProperty);
        }
    }
}
