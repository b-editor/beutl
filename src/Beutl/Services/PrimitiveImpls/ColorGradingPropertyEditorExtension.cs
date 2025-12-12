using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;

using Beutl.Controls.PropertyEditors;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.ViewModels.Editors.Specialized;

namespace Beutl.Services.PrimitiveImpls;

public sealed class ColorGradingPropertyEditorExtension : PropertyEditorExtension
{
    public static readonly ColorGradingPropertyEditorExtension Instance = new();

    public override string Name => nameof(ColorGradingPropertyEditorExtension);

    public override string DisplayName => "ColorGradingColorWheel";

    public override IEnumerable<IPropertyAdapter> MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        foreach (IPropertyAdapter property in properties)
        {
            if (property.PropertyType == typeof(Color)
                && property.ImplementedType.IsAssignableTo(typeof(ColorGrading)))
            {
                yield return property;
                yield break;
            }
        }
    }

    public override bool TryCreateContext(IReadOnlyList<IPropertyAdapter> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        // ImeplementedTypeがColorGradingのColorプロパティに対してColorWheelEditorViewModelを返す
        foreach (IPropertyAdapter property in properties)
        {
            if (property.PropertyType == typeof(Color)
                && property.ImplementedType.IsAssignableTo(typeof(ColorGrading)))
            {
                context = new ColorWheelEditorViewModel((IPropertyAdapter<Color>)property);
                return true;
            }
        }

        context = null;
        return false;
    }

    public override bool TryCreateControl(IPropertyEditorContext context, out Control? control)
    {
        if (context is ColorWheelEditorViewModel)
        {
            control = new ColorWheelEditor();

            return true;
        }

        control = null;
        return false;
    }
}
