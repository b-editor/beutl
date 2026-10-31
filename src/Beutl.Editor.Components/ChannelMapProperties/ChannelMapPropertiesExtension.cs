using Beutl.Audio;
using Beutl.Editor.Components.ChannelMapProperties.ViewModels;
using Beutl.Editor.Components.ChannelMapProperties.Views;

namespace Beutl.Editor.Components.ChannelMapProperties;

public sealed class ChannelMapPropertiesExtension : PropertyEditorExtension
{
    public static new readonly ChannelMapPropertiesExtension Instance = new();

    protected internal override void RegisterEditors(PropertyEditorRegistry registry)
    {
        registry.Register<AudioChannelMap, ChannelMapEditorViewModel>(
            property => new ChannelMapEditorViewModel(property), _ => new ChannelMapEditor());
    }
}
