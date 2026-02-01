using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.ColorGradingProperties.ViewModels;
using Beutl.Editor.Components.ColorGradingProperties.Views;
using Beutl.Extensibility;
using Beutl.Graphics.Effects;

namespace Beutl.Editor.Components.ColorGradingProperties;

[PrimitiveImpl]
public sealed class ColorGradingPropertiesExtension : PropertyEditorExtension
{
    public static new readonly ColorGradingPropertiesExtension Instance = new();

    public override IEnumerable<IPropertyAdapter> MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        try
        {
            var colorGradingProps = properties.Where(p => p.ImplementedType == typeof(ColorGrading)).ToArray();
            if (colorGradingProps.Length > 1)
            {
                return colorGradingProps;
            }
        }
        catch
        {
        }

        return [];
    }

    public override bool TryCreateContext(IReadOnlyList<IPropertyAdapter> properties,
        [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        if (properties.Count <= 1
            || properties[0].ImplementedType != typeof(ColorGrading))
        {
            context = null;
            return false;
        }

        context = new ColorGradingPropertiesViewModel(properties);
        return true;
    }

    public override bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
    {
        control = new ColorGradingPropertiesEditor();
        return true;
    }
}
