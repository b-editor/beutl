using Beutl.Controls.PropertyEditors;
using Beutl.Media;

namespace Beutl.ViewModels.Editors;

public sealed class AlignmentXEditorViewModel(IPropertyAdapter<AlignmentX> property) : ValueEditorViewModel<AlignmentX>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is AlignmentXEditor view && !Disposables.IsDisposed)
        {
            AttachConfirmedValueBinding(view, AlignmentXEditor.ValueProperty);
        }
    }
}
