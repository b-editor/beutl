using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.Media.Encoding;
using Beutl.PropertyAdapters;
using FFmpeg.AutoGen.Abstractions;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

public sealed class FFmpegEncoderSpecializedPropertyExtension : PropertyEditorExtension
{
    private static bool IsSampleRateProperty(IPropertyAdapter prop)
    {
        return prop is CorePropertyAdapter<int> cpa
               && cpa.Property.Id == AudioEncoderSettings.SampleRateProperty.Id
               && cpa.Object is FFmpegAudioEncoderSettings;
    }

    private static bool IsPixelFormatProperty(IPropertyAdapter prop)
    {
        return prop is CorePropertyAdapter<AVPixelFormat> cpa
               && cpa.Property.Id == FFmpegVideoEncoderSettings.FormatProperty.Id
               && cpa.Object is FFmpegVideoEncoderSettings;
    }

    private static bool IsAudioFormatProperty(IPropertyAdapter prop)
    {
        return prop is CorePropertyAdapter<FFmpegAudioEncoderSettings.AudioFormat> cpa
               && cpa.Property.Id == FFmpegAudioEncoderSettings.FormatProperty.Id
               && cpa.Object is FFmpegAudioEncoderSettings;
    }

    public override IEnumerable<IPropertyAdapter> MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        foreach (var prop in properties)
        {
            if (IsAudioFormatProperty(prop) || IsPixelFormatProperty(prop) || IsSampleRateProperty(prop))
            {
                return [prop];
            }
        }

        return [];
    }

    public override bool TryCreateContext(
        IReadOnlyList<IPropertyAdapter> properties,
        [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        context = null;
        if (properties.Count != 1)
        {
            return false;
        }

        var prop = properties[0];
        if (IsSampleRateProperty(prop))
        {
            context = new SampleRateEditorViewModel((IPropertyAdapter<int>)prop, this);
        }
        else if (IsAudioFormatProperty(prop))
        {
            context = new AudioFormatEditorViewModel(
                (IPropertyAdapter<FFmpegAudioEncoderSettings.AudioFormat>)prop, this);
        }
        else if (IsPixelFormatProperty(prop))
        {
            context = new PixelFormatEditorViewModel((IPropertyAdapter<AVPixelFormat>)prop, this);
        }

        return context != null;
    }

    public override bool TryCreateControl(
        IPropertyEditorContext context,
        [NotNullWhen(true)] out Control? control)
    {
        if (context is SampleRateEditorViewModel)
        {
            var editor = new AutoCompleteStringEditor();
            context.Accept(editor);
            control = editor;
            return true;
        }

        if (context is AudioFormatEditorViewModel or PixelFormatEditorViewModel)
        {
            var editor = new EnumEditor();
            context.Accept(editor);
            control = editor;
            return true;
        }

        return base.TryCreateControl(context, out control);
    }
}
