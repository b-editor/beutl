using System.ComponentModel.DataAnnotations;
using System.Runtime.Versioning;
using Beutl.Extensibility;
using Beutl.Extensions.MediaFoundation.Properties;
using Beutl.Media.Encoding;
using Vortice.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Encoding;
#else
namespace Beutl.Extensions.MediaFoundation.Encoding;
#endif

[Export]
[SupportedOSPlatform("windows")]
[Display(Name = nameof(Strings.EncodingName), ResourceType = typeof(Strings))]
public sealed class MFEncodingExtension : ControllableEncodingExtension
{
    public override IEnumerable<string> SupportExtensions() => MFEncodingFormat.Extensions;

    public override EncodingController CreateController(string file) => new MFEncodingController(file);
}

public sealed class MFVideoEncoderSettings : VideoEncoderSettings
{
    public MFVideoEncoderSettings()
    {
        ScanProperties<MFVideoEncoderSettings>();
    }

    [Display(Name = nameof(Strings.Codec), Description = nameof(Strings.VideoCodecDescription), ResourceType = typeof(Strings))]
    public IProperty<MFVideoCodec> Codec { get; } = Property.CreateValue(MFVideoCodec.Auto);

    [Display(Name = nameof(Strings.UseHardwareAcceleration), ResourceType = typeof(Strings))]
    public IProperty<bool> UseHardwareAcceleration { get; } = Property.CreateValue(true);
}

public enum MFVideoCodec
{
    [Display(Name = nameof(Strings.AutoByExtension), ResourceType = typeof(Strings))] Auto = 0,
    [Display(Name = "H.264 / AVC")] H264 = 1,
    [Display(Name = "H.265 / HEVC")] HEVC = 2,
    [Display(Name = "WMV 7")] WMV7 = 3,
    [Display(Name = "WMV 8")] WMV8 = 4,
    [Display(Name = "WMV 9")] WMV9 = 5,
    [Display(Name = "VC-1")] VC1 = 6,
    [Display(Name = "RGB32 (AVI)")] RGB32 = 7,
    [Display(Name = nameof(Strings.DisableStream), ResourceType = typeof(Strings))] None = 8,
}

public sealed class MFAudioEncoderSettings : AudioEncoderSettings
{
    public MFAudioEncoderSettings()
    {
        ScanProperties<MFAudioEncoderSettings>();
    }

    [Display(Name = nameof(Strings.Codec), Description = nameof(Strings.AudioCodecDescription), ResourceType = typeof(Strings))]
    public IProperty<MFAudioCodec> Codec { get; } = Property.CreateValue(MFAudioCodec.Auto);

    [Display(Name = nameof(Strings.BitDepth), Description = nameof(Strings.BitDepthDescription), ResourceType = typeof(Strings))]
    public IProperty<MFAudioBitDepth> BitDepth { get; } = Property.CreateValue(MFAudioBitDepth.Bit16);
}

public enum MFAudioCodec
{
    [Display(Name = nameof(Strings.AutoByExtension), ResourceType = typeof(Strings))] Auto = 0,
    [Display(Name = "AAC")] AAC = 1,
    [Display(Name = "MP3")] MP3 = 2,
    [Display(Name = "WMA")] WMA = 3,
    [Display(Name = "WMA Pro")] WMAPro = 4,
    [Display(Name = "FLAC")] FLAC = 5,
    [Display(Name = "ALAC")] ALAC = 6,
    [Display(Name = "PCM")] PCM = 7,
    [Display(Name = "PCM (32-bit float)")] Float = 8,
    [Display(Name = nameof(Strings.DisableStream), ResourceType = typeof(Strings))] None = 9,
}

public enum MFAudioBitDepth
{
    [Display(Name = "8-bit")] Bit8 = 8,
    [Display(Name = "16-bit")] Bit16 = 16,
    [Display(Name = "24-bit")] Bit24 = 24,
    [Display(Name = "32-bit")] Bit32 = 32,
}

// Keep defaults and compatibility in one place. Auto follows the extension even when settings
// are copied to a controller for another file. Only list combinations supported by the native sinks.
internal sealed record MFEncodingFormat(
    Guid ContainerType, MFVideoCodec[] VideoCodecs, MFAudioCodec[] AudioCodecs, bool RequiresVideo = false)
{
    private static readonly MFEncodingFormat Mp4 = new(TranscodeContainerTypeGuids.Mpeg4,
        [MFVideoCodec.H264, MFVideoCodec.HEVC], [MFAudioCodec.AAC, MFAudioCodec.MP3, MFAudioCodec.ALAC]);
    private static readonly MFEncodingFormat ThreeGp = new(TranscodeContainerTypeGuids.Tc3gp,
        [MFVideoCodec.H264], [MFAudioCodec.AAC]);
    private static readonly MFEncodingFormat Asf = new(TranscodeContainerTypeGuids.Asf,
        [MFVideoCodec.WMV9, MFVideoCodec.WMV7, MFVideoCodec.WMV8, MFVideoCodec.VC1],
        [MFAudioCodec.WMA, MFAudioCodec.WMAPro]);

    private static readonly Dictionary<string, MFEncodingFormat> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = Mp4,
        [".m4v"] = Mp4,
        [".mov"] = Mp4,
        [".m4a"] = Mp4 with { VideoCodecs = [] },
        [".3gp"] = ThreeGp,
        [".3gpp"] = ThreeGp,
        [".3g2"] = ThreeGp,
        [".3gp2"] = ThreeGp,
        [".wmv"] = Asf,
        [".asf"] = Asf,
        [".wma"] = Asf with { VideoCodecs = [] },
        [".avi"] = new(TranscodeContainerTypeGuids.Avi, [MFVideoCodec.RGB32], [MFAudioCodec.PCM], RequiresVideo: true),
        [".mp3"] = new(TranscodeContainerTypeGuids.Mp3, [], [MFAudioCodec.MP3]),
        [".wav"] = new(TranscodeContainerTypeGuids.Wave, [], [MFAudioCodec.PCM, MFAudioCodec.Float]),
        [".aac"] = new(TranscodeContainerTypeGuids.Adts, [], [MFAudioCodec.AAC]),
        [".adts"] = new(TranscodeContainerTypeGuids.Adts, [], [MFAudioCodec.AAC]),
        [".flac"] = new(TranscodeContainerTypeGuids.Flac, [], [MFAudioCodec.FLAC]),
    };

    public static IEnumerable<string> Extensions => Formats.Keys;

    public static MFEncodingFormat FromFile(string file)
        => Formats.TryGetValue(Path.GetExtension(file), out var format)
            ? format : throw new NotSupportedException($"Media Foundation encoding does not support '{Path.GetExtension(file)}' output.");

    public MFVideoCodec ResolveVideoCodec(MFVideoCodec codec)
    {
        // Audio-only containers ignore video settings and never request frames from the provider.
        if (VideoCodecs.Length == 0 || codec == MFVideoCodec.None)
            return MFVideoCodec.None;
        if (codec == MFVideoCodec.Auto)
            return VideoCodecs[0];
        if (!VideoCodecs.Contains(codec))
            throw new ArgumentException($"This container cannot store {codec} video. Choose Auto or one of: {string.Join(", ", VideoCodecs)}.");
        return codec;
    }

    public MFAudioCodec ResolveAudioCodec(MFAudioCodec codec)
    {
        if (codec == MFAudioCodec.None)
            return codec;
        if (codec == MFAudioCodec.Auto)
            return AudioCodecs[0];
        if (!AudioCodecs.Contains(codec))
            throw new ArgumentException($"This container cannot store {codec} audio. Choose Auto or one of: {string.Join(", ", AudioCodecs)}.");
        return codec;
    }

    public static Guid VideoSubtype(MFVideoCodec codec) => codec switch
    {
        MFVideoCodec.H264 => VideoFormatGuids.H264,
        MFVideoCodec.HEVC => VideoFormatGuids.Hevc,
        MFVideoCodec.WMV7 => VideoFormatGuids.Wmv1,
        MFVideoCodec.WMV8 => VideoFormatGuids.Wmv2,
        MFVideoCodec.WMV9 => VideoFormatGuids.Wmv3,
        MFVideoCodec.VC1 => VideoFormatGuids.Wvc1,
        MFVideoCodec.RGB32 => VideoFormatGuids.Rgb32,
        _ => throw new ArgumentOutOfRangeException(nameof(codec)),
    };

    public static Guid AudioSubtype(MFAudioCodec codec) => codec switch
    {
        MFAudioCodec.AAC => AudioFormatGuids.Aac,
        MFAudioCodec.MP3 => AudioFormatGuids.Mp3,
        MFAudioCodec.WMA => AudioFormatGuids.WMAudioV8,
        MFAudioCodec.WMAPro => AudioFormatGuids.WMAudioV9,
        MFAudioCodec.FLAC => AudioFormatGuids.Flac,
        MFAudioCodec.ALAC => AudioFormatGuids.Alac,
        MFAudioCodec.PCM => AudioFormatGuids.Pcm,
        MFAudioCodec.Float => AudioFormatGuids.Float,
        _ => throw new ArgumentOutOfRangeException(nameof(codec)),
    };
}
