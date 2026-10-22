using Beutl.Editor.Components.ColorGradingProperties.ViewModels;
using Beutl.Editor.Components.ColorGradingProperties.Views;
using Beutl.Graphics.Effects;

namespace Beutl.Editor.Components.ColorGradingProperties;

public sealed class ColorGradingPropertiesExtension : PropertyEditorExtension
{
    public static new readonly ColorGradingPropertiesExtension Instance = new();

    protected internal override void RegisterEditors(PropertyEditorRegistry registry)
    {
        // ImplementedType throws for a list item's adapter, so match on the owner instead.
        registry.RegisterGroup(
            properties =>
            {
                IPropertyAdapter[] colorGradingProps = [.. properties.Where(p => p.GetEngineProperty()?.GetOwnerObject() is ColorGrading)];
                return colorGradingProps.Length > 1 ? colorGradingProps : [];
            },
            properties => new ColorGradingPropertiesViewModel(properties),
            _ => new ColorGradingPropertiesEditor());
    }
}
