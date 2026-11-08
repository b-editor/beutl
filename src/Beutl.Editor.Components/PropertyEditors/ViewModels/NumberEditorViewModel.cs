using System.ComponentModel.DataAnnotations;
using System.Globalization;
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

            if (editor is NumberSliderEditor<T> slider)
                ApplyRange(slider);
        }
    }

    // The slider spans the property's RangeAttribute and starts its fill at the default value.
    private void ApplyRange(NumberSliderEditor<T> slider)
    {
        if (PropertyAdapter.GetAttributes().OfType<RangeAttribute>().FirstOrDefault() is { } range)
        {
            slider.Minimum = Convert.ToDouble(range.Minimum, CultureInfo.InvariantCulture);
            slider.Maximum = Convert.ToDouble(range.Maximum, CultureInfo.InvariantCulture);
        }

        slider.Origin = GetDefaultValue() is T value ? double.CreateTruncating(value) : 0;
    }
}
