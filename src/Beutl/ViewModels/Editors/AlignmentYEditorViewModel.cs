using Beutl.Controls.PropertyEditors;
using Beutl.Media;

namespace Beutl.ViewModels.Editors;

public sealed class AlignmentYEditorViewModel(IPropertyAdapter<AlignmentY> property) : ValueEditorViewModel<AlignmentY>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is AlignmentYEditor view && !Disposables.IsDisposed)
        {
            AttachConfirmedValueBinding(view, AlignmentYEditor.ValueProperty);
        }
    }
}
