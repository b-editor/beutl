using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class TimeSpanEditorViewModel(IPropertyAdapter<TimeSpan> property) : ValueEditorViewModel<TimeSpan>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is TimeSpanEditor editor && !Disposables.IsDisposed)
        {
            AttachValueBindings(
                editor,
                TimeSpanEditor.ValueProperty,
                static ed => ed.Value,
                static (ed, v) => ed.Value = v);
        }
    }
}
