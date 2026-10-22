using System.ComponentModel.DataAnnotations;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class StringEditorViewModel(IPropertyAdapter<string?> property) : PropertyEditorViewModel<string?, string>(property)
{
    protected override string ToEdit(string? value) => value ?? string.Empty;

    protected override string? FromEdit(string edit) => edit;

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is StringEditor editor && !Disposables.IsDisposed)
        {
            var attrs = PropertyAdapter.GetAttributes();
            bool multiline = attrs.Any(v => v is DataTypeAttribute { DataType: DataType.MultilineText });
            editor.Classes.Set("multiline", multiline);
        }
    }
}
