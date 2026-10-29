using System.Numerics;
using Beutl.Controls.PropertyEditors;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class NumberEditorViewModel<T>(IPropertyAdapter<T> property) : ValueEditorViewModel<T>(property)
    where T : struct, INumber<T>
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is NumberEditor<T> editor && !Disposables.IsDisposed)
        {
            editor.HapticReferenceValue = GetDefaultValue() is T value ? value : T.Zero;
            VectorEditorBindingHelper.ApplyNumberAttributes<T>(
                PropertyAdapter,
                (large, small) =>
                {
                    editor.LargeChange = large;
                    editor.SmallChange = small;
                },
                format => editor.NumberFormat = format);
        }
    }
}
