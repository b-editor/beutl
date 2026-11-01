using Beutl.Audio;
using Beutl.Editor.Components.SoundPannerProperties.ViewModels;
using Beutl.Editor.Components.SoundPannerProperties.Views;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.Components.SoundPannerProperties;

public sealed class SoundPannerPropertiesExtension : PropertyEditorExtension
{
    public static new readonly SoundPannerPropertiesExtension Instance = new();

    protected internal override void RegisterEditors(PropertyEditorRegistry registry)
    {
        registry.RegisterGroup(
            properties =>
            {
                // Wait until the panel reaches these properties instead of taking them ahead of earlier rows.
                if (properties.FirstOrDefault()?.GetEngineProperty()?.Name is not
                    (nameof(Sound.Pan) or nameof(Sound.SurroundPan) or nameof(Sound.CenterLevel) or nameof(Sound.LfeLevel)))
                    return [];

                // List item adapters need not implement ImplementedType.
                foreach (var group in properties
                    .Where(p => p.GetEngineProperty()?.GetOwnerObject() is Sound)
                    .GroupBy(p => p.GetEngineProperty()!.GetOwnerObject()))
                {
                    IPropertyAdapter[] matched = [.. group.Where(p => p.GetEngineProperty()!.Name is
                        nameof(Sound.Pan) or nameof(Sound.SurroundPan) or nameof(Sound.CenterLevel) or nameof(Sound.LfeLevel))];
                    if (matched.Select(p => p.GetEngineProperty()!.Name).Distinct().Count() == 4)
                        return matched;
                }
                return [];
            },
            properties => new SoundPannerViewModel(properties),
            _ => new SoundPannerEditor());
    }
}
