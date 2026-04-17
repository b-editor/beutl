using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class NumberEditorViewModel<T>(IPropertyAdapter<T> property) : ValueEditorViewModel<T>(property)
    where T : struct, INumber<T>
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is NumberEditor<T> editor && !Disposables.IsDisposed)
        {
            var attrs = PropertyAdapter.GetAttributes();
            var stepAttr = attrs.OfType<NumberStepAttribute>().FirstOrDefault();
            if (stepAttr != null)
            {
                editor.LargeChange = T.CreateTruncating(stepAttr.LargeChange);
                editor.SmallChange = T.CreateTruncating(stepAttr.SmallChange);
            }
            var formatAttr = attrs.OfType<DisplayFormatAttribute>().FirstOrDefault();
            if (formatAttr != null)
            {
                editor.NumberFormat = formatAttr.DataFormatString;
            }

            AttachValueBindings(
                editor,
                NumberEditor<T>.ValueProperty,
                static ed => ed.Value,
                static (ed, v) => ed.Value = v);
        }
    }
}
