using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Numerics;
using Beutl.Controls.PropertyEditors;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class NumberEditorViewModel<T>(IPropertyAdapter<T> property) : ValueEditorViewModel<T>(property)
    where T : struct, INumber<T>
{
    // The most small steps a slider spans. Past that, a step is far less than a pixel of its thumb's travel, so that the
    // thumb shows nothing of a drag.
    private const double MaxSliderSteps = 2000;

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

    // The slider spans the property's RangeAttribute and starts its fill at the default value. A property with no range
    // for a slider has its two ends meet, and its box takes a drag without showing a fill or a thumb.
    private void ApplyRange(NumberSliderEditor<T> slider)
    {
        bool hasRange = TryGetSliderRange(out double minimum, out double maximum);
        slider.Minimum = hasRange ? minimum : 0;
        slider.Maximum = hasRange ? maximum : 0;
        slider.Origin = GetDefaultValue() is T value ? double.CreateTruncating(value) : 0;
    }

    // Whether a slider suits the property: its RangeAttribute gives both ends, with room between them for a few steps
    // and not for more than a slider can show.
    private bool TryGetSliderRange(out double minimum, out double maximum)
    {
        if (!TryGetRange(out minimum, out maximum))
            return false;

        double step = PropertyAdapter.GetAttributes().OfType<NumberStepAttribute>().FirstOrDefault()?.SmallChange ?? 1;
        double steps = (maximum - minimum) / step;
        return steps is >= 2 and <= MaxSliderSteps;
    }

    // The bounds of the property's RangeAttribute; one given as text, such as "max", is not a number yet.
    private bool TryGetRange(out double minimum, out double maximum)
    {
        if (PropertyAdapter.GetAttributes().OfType<RangeAttribute>().FirstOrDefault() is
            { Minimum: IConvertible and not string, Maximum: IConvertible and not string } range)
        {
            minimum = Convert.ToDouble(range.Minimum, CultureInfo.InvariantCulture);
            maximum = Convert.ToDouble(range.Maximum, CultureInfo.InvariantCulture);
            return true;
        }

        minimum = maximum = 0;
        return false;
    }
}
