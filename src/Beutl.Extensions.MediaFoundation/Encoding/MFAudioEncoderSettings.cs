using System.ComponentModel.DataAnnotations;
using Beutl.Media.Encoding;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Encoding;
#else
namespace Beutl.Extensions.MediaFoundation.Encoding;
#endif

public sealed class MFAudioEncoderSettings : AudioEncoderSettings
{
    public static readonly CoreProperty<AudioCodecType> CodecProperty;

    static MFAudioEncoderSettings()
    {
        CodecProperty = ConfigureProperty<AudioCodecType, MFAudioEncoderSettings>(nameof(Codec))
            .DefaultValue(AudioCodecType.AAC)
            .Register();
    }

    [Display(Name = "Codec")]
    public AudioCodecType Codec
    {
        get => GetValue(CodecProperty);
        set => SetValue(CodecProperty, value);
    }

    // Restricted to codecs the Media Foundation Sink Writer can mux into
    // our supported containers (MP4/MOV/ASF/Wave). WMA is only meaningful
    // inside ASF and WAV implies PCM; both are provided for completeness.
    public enum AudioCodecType
    {
        [Display(Name = "AAC")] AAC = 0,
        [Display(Name = "MP3")] MP3 = 1,
        [Display(Name = "WMA")] WMA = 2,
        [Display(Name = "PCM (uncompressed)")] PCM = 3,
    }
}
