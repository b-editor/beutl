using Beutl.Audio.Effects;
using Beutl.Editor.Components.CompressorProperties.ViewModels;
using Beutl.Editor.Components.CompressorProperties.Views;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.Components.CompressorProperties;

public sealed class CompressorPropertiesExtension : PropertyEditorExtension
{
    public static new readonly CompressorPropertiesExtension Instance = new();

    protected internal override void RegisterEditors(PropertyEditorRegistry registry)
    {
        // ImplementedType throws for a list item's adapter, so match on the owner instead. The group
        // replaces every row of one compressor with its curve followed by the same number editors.
        registry.RegisterGroup(
            properties =>
            {
                foreach (IGrouping<CoreObject, IPropertyAdapter> group in properties
                             .Where(p => p.GetEngineProperty()?.GetOwnerObject() is CompressorEffect)
                             .GroupBy(p => p.GetEngineProperty()!.GetOwnerObject()!))
                {
                    var compressor = (CompressorEffect)group.Key;
                    IPropertyAdapter[] matched = [.. group];
                    if (matched.Any(p => ReferenceEquals(p.GetEngineProperty(), compressor.Threshold))
                        && matched.Any(p => ReferenceEquals(p.GetEngineProperty(), compressor.Ratio)))
                    {
                        return matched;
                    }
                }

                return [];
            },
            properties => new CompressorPropertiesViewModel(properties),
            _ => new CompressorPropertiesEditor());
    }
}
