using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Graphics.Effects;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ColorGradingPropertiesExtension : PropertyEditorExtension
{
    public static readonly ColorGradingPropertiesExtension Instance = new();

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
