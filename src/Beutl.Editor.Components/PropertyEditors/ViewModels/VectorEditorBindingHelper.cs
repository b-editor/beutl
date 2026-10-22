using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Controls.PropertyEditors;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

internal static class VectorEditorBindingHelper
{
    // Passes the steps of the property's NumberStepAttribute and the format of its DisplayFormatAttribute to the
    // setters, skipping an attribute the property does not have.
    public static void ApplyNumberAttributes<TElement>(
        IPropertyAdapter adapter,
        Action<TElement, TElement> setSteps,
        Action<string?> setFormat)
        where TElement : INumber<TElement>
    {
        var attrs = adapter.GetAttributes();
        var stepAttr = attrs.OfType<NumberStepAttribute>().FirstOrDefault();
        if (stepAttr != null)
        {
            setSteps(TElement.CreateTruncating(stepAttr.LargeChange), TElement.CreateTruncating(stepAttr.SmallChange));
        }

        var formatAttr = attrs.OfType<DisplayFormatAttribute>().FirstOrDefault();
        if (formatAttr != null)
        {
            setFormat(formatAttr.DataFormatString);
        }
    }

    public static void ApplyNumberAttributes<TElement>(Vector2Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
        => ApplyNumberAttributes<TElement>(
            adapter,
            (large, small) =>
            {
                editor.LargeChange = large;
                editor.SmallChange = small;
            },
            format => editor.NumberFormat = format);

    public static void ApplyNumberAttributes<TElement>(Vector3Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
        => ApplyNumberAttributes<TElement>(
            adapter,
            (large, small) =>
            {
                editor.LargeChange = large;
                editor.SmallChange = small;
            },
            format => editor.NumberFormat = format);

    public static void ApplyNumberAttributes<TElement>(Vector4Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
        => ApplyNumberAttributes<TElement>(
            adapter,
            (large, small) =>
            {
                editor.LargeChange = large;
                editor.SmallChange = small;
            },
            format => editor.NumberFormat = format);
}
