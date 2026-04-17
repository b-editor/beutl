using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class RationalEditorViewModel(IPropertyAdapter<Rational> property) : ValueEditorViewModel<Rational>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is RationalEditor editor && !Disposables.IsDisposed)
        {
            AttachValueBindings(
                editor,
                RationalEditor.ValueProperty,
                static ed => ed.Value,
                static (ed, v) => ed.Value = v);
        }
    }
}
