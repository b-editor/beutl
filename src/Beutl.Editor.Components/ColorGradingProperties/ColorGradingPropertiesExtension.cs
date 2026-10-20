using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.ColorGradingProperties.ViewModels;
using Beutl.Editor.Components.ColorGradingProperties.Views;
using Beutl.Graphics.Effects;

namespace Beutl.Editor.Components.ColorGradingProperties;

public sealed class ColorGradingPropertiesExtension : PropertyEditorExtension
{
    public static new readonly ColorGradingPropertiesExtension Instance = new();

    public override IEnumerable<IPropertyAdapter> MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        // ImplementedType throws for a list item's adapter, so match on the owner instead.
        var colorGradingProps = properties.Where(p => p.GetEngineProperty()?.GetOwnerObject() is ColorGrading).ToArray();
        return colorGradingProps.Length > 1 ? colorGradingProps : [];
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
