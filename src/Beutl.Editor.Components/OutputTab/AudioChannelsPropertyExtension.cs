using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Media.Encoding;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.Components.OutputTab;

/// <summary>Offers the common speaker layouts without changing saved channel counts.</summary>
public sealed class AudioChannelsPropertyExtension : PropertyEditorExtension
{
    public static new readonly AudioChannelsPropertyExtension Instance = new();

    protected internal override void RegisterEditors(PropertyEditorRegistry registry)
    {
        registry.Register<int, AudioChannelsEditorViewModel>(
            property => new AudioChannelsEditorViewModel(property,
                (AudioEncoderSettings)((EnginePropertyAdapter<int>)property).Object),
            CreateControl,
            new PropertyEditorOptions
            {
                Match = property => property is EnginePropertyAdapter<int> { Object: AudioEncoderSettings settings }
                    && ReferenceEquals(property.GetEngineProperty(), settings.Channels),
                CreateSettingsControl = CreateControl,
            });
    }

    private static Control CreateControl(IPropertyEditorContext context)
    {
        var editor = new EnumEditor();
        PropertyEditorControls.Attach(context, editor);
        return editor;
    }
}
