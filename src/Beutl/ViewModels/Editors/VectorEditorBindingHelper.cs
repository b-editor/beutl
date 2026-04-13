using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

internal static class VectorEditorBindingHelper
{
    public static void ApplyNumberAttributes<TElement>(Vector2Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
    {
        var attrs = adapter.GetAttributes();
        var stepAttr = attrs.OfType<NumberStepAttribute>().FirstOrDefault();
        if (stepAttr != null)
        {
            editor.LargeChange = TElement.CreateTruncating(stepAttr.LargeChange);
            editor.SmallChange = TElement.CreateTruncating(stepAttr.SmallChange);
        }

        var formatAttr = attrs.OfType<DisplayFormatAttribute>().FirstOrDefault();
        if (formatAttr != null)
        {
            editor.NumberFormat = formatAttr.DataFormatString;
        }
    }

    public static void ApplyNumberAttributes<TElement>(Vector3Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
    {
        var attrs = adapter.GetAttributes();
        var stepAttr = attrs.OfType<NumberStepAttribute>().FirstOrDefault();
        if (stepAttr != null)
        {
            editor.LargeChange = TElement.CreateTruncating(stepAttr.LargeChange);
            editor.SmallChange = TElement.CreateTruncating(stepAttr.SmallChange);
        }

        var formatAttr = attrs.OfType<DisplayFormatAttribute>().FirstOrDefault();
        if (formatAttr != null)
        {
            editor.NumberFormat = formatAttr.DataFormatString;
        }
    }

    public static void ApplyNumberAttributes<TElement>(Vector4Editor<TElement> editor, IPropertyAdapter adapter)
        where TElement : INumber<TElement>
    {
        var attrs = adapter.GetAttributes();
        var stepAttr = attrs.OfType<NumberStepAttribute>().FirstOrDefault();
        if (stepAttr != null)
        {
            editor.LargeChange = TElement.CreateTruncating(stepAttr.LargeChange);
            editor.SmallChange = TElement.CreateTruncating(stepAttr.SmallChange);
        }

        var formatAttr = attrs.OfType<DisplayFormatAttribute>().FirstOrDefault();
        if (formatAttr != null)
        {
            editor.NumberFormat = formatAttr.DataFormatString;
        }
    }
}
