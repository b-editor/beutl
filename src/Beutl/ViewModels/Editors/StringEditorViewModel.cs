using System.ComponentModel.DataAnnotations;
using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class StringEditorViewModel(IPropertyAdapter<string?> property) : ValueEditorViewModel<string?>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is StringEditor editor && !Disposables.IsDisposed)
        {
            var attrs = PropertyAdapter.GetAttributes();
            bool multiline = attrs.Any(v => v is DataTypeAttribute { DataType: DataType.MultilineText });

            AttachValueBindings(
                editor,
                StringEditor.TextProperty,
                static ed => ed.Text,
                static (ed, v) => ed.Text = v);
            editor.Classes.Set("multiline", multiline);
        }
    }
}
