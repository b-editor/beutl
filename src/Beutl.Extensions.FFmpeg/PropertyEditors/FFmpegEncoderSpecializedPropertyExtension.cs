using Avalonia.Controls;
using Beutl.Collections;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Media.Encoding;
using Beutl.PropertyAdapters;
using AudioFormat = Beutl.Extensions.FFmpeg.Encoding.FFmpegAudioEncoderSettings.AudioFormat;
namespace Beutl.Extensions.FFmpeg.PropertyEditors;

[Export]
public sealed class FFmpegEncoderSpecializedPropertyExtension : PropertyEditorExtension
{
    private static bool IsVideoOptionsProperty(IPropertyAdapter prop)
        => prop is EnginePropertyAdapter<CoreList<AdditionalOption>> { Object: FFmpegVideoEncoderSettings settings } adapter
            && adapter.Property == settings.Options;

    private static bool IsSampleRateProperty(IPropertyAdapter prop)
    {
        return prop is EnginePropertyAdapter<int> { Object: FFmpegAudioEncoderSettings settings } adapter
               && adapter.Property == settings.SampleRate;
    }

    private static bool IsPixelFormatProperty(IPropertyAdapter prop)
    {
        return prop is EnginePropertyAdapter<int> { Object: FFmpegVideoEncoderSettings settings } adapter
               && adapter.Property == settings.Format;
    }

    private static bool IsAudioFormatProperty(IPropertyAdapter prop)
    {
        return prop is EnginePropertyAdapter<AudioFormat> { Object: FFmpegAudioEncoderSettings settings } adapter
               && adapter.Property == settings.Format;
    }

    protected override void RegisterEditors(PropertyEditorRegistry registry)
    {
        registry.Register<CoreList<AdditionalOption>, EncoderOptionsEditorViewModel>(
            p => new EncoderOptionsEditorViewModel((EnginePropertyAdapter<CoreList<AdditionalOption>>)p, this),
            context => new EncoderOptionsEditor(context),
            new PropertyEditorOptions { Match = IsVideoOptionsProperty });
        registry.Register<int, SampleRateEditorViewModel>(
            p => new SampleRateEditorViewModel(p, this),
            context => Accept(context, new AutoCompleteStringEditor()),
            new PropertyEditorOptions { Match = IsSampleRateProperty });
        registry.Register<AudioFormat, AudioFormatEditorViewModel>(
            p => new AudioFormatEditorViewModel(p, this),
            context => Accept(context, new EnumEditor()),
            new PropertyEditorOptions { Match = IsAudioFormatProperty });
        registry.Register<int, PixelFormatEditorViewModel>(
            p => new PixelFormatEditorViewModel(p, this),
            context => Accept(context, new EnumEditor()),
            new PropertyEditorOptions { Match = IsPixelFormatProperty });
    }

    // These view models bind the editor themselves when it visits them.
    private static Control Accept(IPropertyEditorContext context, PropertyEditor editor)
    {
        context.Accept(editor);
        return editor;
    }
}
