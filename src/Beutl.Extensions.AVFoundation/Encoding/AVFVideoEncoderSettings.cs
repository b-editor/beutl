using Beutl.Media.Encoding;

namespace Beutl.Extensions.AVFoundation.Encoding;

public sealed class AVFAudioEncoderSettings : AudioEncoderSettings
{
    public static readonly CoreProperty<AudioFormatType> FormatProperty;
    public static readonly CoreProperty<BitDepth> LinearPcmBitDepthProperty;
    public static readonly CoreProperty<bool> LinearPcmBigEndianProperty;
    public static readonly CoreProperty<bool> LinearPcmFloatProperty;
    public static readonly CoreProperty<bool> LinearPcmNonInterleavedProperty;
    public static readonly CoreProperty<AudioQuality> QualityProperty;
    public static readonly CoreProperty<AudioQuality> SampleRateConverterQualityProperty;

    static AVFAudioEncoderSettings()
    {
        FormatProperty = ConfigureProperty<AudioFormatType, AVFAudioEncoderSettings>(nameof(Format))
            .DefaultValue(AudioFormatType.MPEG4AAC)
            .Register();

        LinearPcmBitDepthProperty = ConfigureProperty<BitDepth, AVFAudioEncoderSettings>(nameof(LinearPcmBitDepth))
            .DefaultValue(BitDepth.Bits16)
            .Register();

        LinearPcmBigEndianProperty = ConfigureProperty<bool, AVFAudioEncoderSettings>(nameof(LinearPcmBigEndian))
            .DefaultValue(false)
            .Register();

        LinearPcmFloatProperty = ConfigureProperty<bool, AVFAudioEncoderSettings>(nameof(LinearPcmFloat))
            .DefaultValue(false)
            .Register();

        LinearPcmNonInterleavedProperty =
            ConfigureProperty<bool, AVFAudioEncoderSettings>(nameof(LinearPcmNonInterleaved))
                .DefaultValue(false)
                .Register();

        QualityProperty = ConfigureProperty<AudioQuality, AVFAudioEncoderSettings>(nameof(Quality))
            .DefaultValue(AudioQuality.Default)
            .Register();

        SampleRateConverterQualityProperty =
            ConfigureProperty<AudioQuality, AVFAudioEncoderSettings>(nameof(SampleRateConverterQuality))
                .DefaultValue(AudioQuality.Default)
                .Register();

        BitrateProperty.OverrideDefaultValue<AVFAudioEncoderSettings>(-1);
    }

    public AudioFormatType Format
    {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public BitDepth LinearPcmBitDepth
    {
        get => GetValue(LinearPcmBitDepthProperty);
        set => SetValue(LinearPcmBitDepthProperty, value);
    }

    public bool LinearPcmBigEndian
    {
        get => GetValue(LinearPcmBigEndianProperty);
        set => SetValue(LinearPcmBigEndianProperty, value);
    }

    public bool LinearPcmFloat
    {
        get => GetValue(LinearPcmFloatProperty);
        set => SetValue(LinearPcmFloatProperty, value);
    }

    public bool LinearPcmNonInterleaved
    {
        get => GetValue(LinearPcmNonInterleavedProperty);
        set => SetValue(LinearPcmNonInterleavedProperty, value);
    }

    public AudioQuality Quality
    {
        get => GetValue(QualityProperty);
        set => SetValue(QualityProperty, value);
    }

    public AudioQuality SampleRateConverterQuality
    {
        get => GetValue(SampleRateConverterQualityProperty);
        set => SetValue(SampleRateConverterQualityProperty, value);
    }

    public enum BitDepth
    {
        Bits8 = 8,
        Bits16 = 16,
        Bits24 = 24,
        Bits32 = 32
    }

    public enum AudioQuality
    {
        Default = -1,
        Min = 0,
        Low = 32, // 0x00000020
        Medium = 64, // 0x00000040
        High = 96, // 0x00000060
        Max = 127, // 0x0000007F
    }

    public enum AudioFormatType
    {
        MPEGLayer1 = 778924081, // 0x2E6D7031
        MPEGLayer2 = 778924082, // 0x2E6D7032
        MPEGLayer3 = 778924083, // 0x2E6D7033
        Audible = 1096107074, // 0x41554442
        MACE3 = 1296122675, // 0x4D414333
        MACE6 = 1296122678, // 0x4D414336
        QDesign2 = 1363430706, // 0x51444D32
        QDesign = 1363430723, // 0x51444D43
        QUALCOMM = 1365470320, // 0x51636C70
        MPEG4AAC = 1633772320, // 0x61616320
        MPEG4AAC_ELD = 1633772389, // 0x61616365
        MPEG4AAC_ELD_SBR = 1633772390, // 0x61616366
        MPEG4AAC_ELD_V2 = 1633772391, // 0x61616367
        MPEG4AAC_HE = 1633772392, // 0x61616368
        MPEG4AAC_LD = 1633772396, // 0x6161636C
        MPEG4AAC_HE_V2 = 1633772400, // 0x61616370
        MPEG4AAC_Spatial = 1633772403, // 0x61616373
        AC3 = 1633889587, // 0x61632D33
        AES3 = 1634038579, // 0x61657333
        AppleLossless = 1634492771, // 0x616C6163
        ALaw = 1634492791, // 0x616C6177
        ParameterValueStream = 1634760307, // 0x61707673
        CAC3 = 1667326771, // 0x63616333
        MPEG4CELP = 1667591280, // 0x63656C70
        MPEG4HVXC = 1752594531, // 0x68767863
        iLBC = 1768710755, // 0x696C6263
        AppleIMA4 = 1768775988, // 0x696D6134
        LinearPCM = 1819304813, // 0x6C70636D
        MIDIStream = 1835623529, // 0x6D696469
        DVIIntelIMA = 1836253201, // 0x6D730011
        MicrosoftGSM = 1836253233, // 0x6D730031
        AMR = 1935764850, // 0x73616D72
        TimeCode = 1953066341, // 0x74696D65
        MPEG4TwinVQ = 1953986161, // 0x74777671
        ULaw = 1970037111, // 0x756C6177
    }
}

public sealed class AVFVideoEncoderSettings : VideoEncoderSettings
{
    public static readonly CoreProperty<VideoCodec> CodecProperty;
    public static readonly CoreProperty<float> JPEGQualityProperty;
    public static readonly CoreProperty<VideoProfileLevelH264> ProfileLevelH264Property;

    static AVFVideoEncoderSettings()
    {
        CodecProperty = ConfigureProperty<VideoCodec, AVFVideoEncoderSettings>(nameof(Codec))
            .DefaultValue(VideoCodec.H264)
            .Register();

        JPEGQualityProperty = ConfigureProperty<float, AVFVideoEncoderSettings>(nameof(JPEGQuality))
            .DefaultValue(-1)
            .Register();

        ProfileLevelH264Property =
            ConfigureProperty<VideoProfileLevelH264, AVFVideoEncoderSettings>(nameof(ProfileLevelH264))
                .DefaultValue(VideoProfileLevelH264.Default)
                .Register();

        BitrateProperty.OverrideDefaultValue<AVFVideoEncoderSettings>(-1);
        KeyframeRateProperty.OverrideDefaultValue<AVFVideoEncoderSettings>(-1);
    }

    public VideoCodec Codec
    {
        get => GetValue(CodecProperty);
        set => SetValue(CodecProperty, value);
    }

    public float JPEGQuality
    {
        get => GetValue(JPEGQualityProperty);
        set => SetValue(JPEGQualityProperty, value);
    }

    public VideoProfileLevelH264 ProfileLevelH264
    {
        get => GetValue(ProfileLevelH264Property);
        set => SetValue(ProfileLevelH264Property, value);
    }

    public enum VideoCodec
    {
        Default = 0,
        H264 = 1,
        JPEG = 2,
    }

    public enum VideoProfileLevelH264
    {
        Default = 0,
        Baseline30 = 1,
        Baseline31 = 2,
        Baseline41 = 3,
        Main30 = 4,
        Main31 = 5,
        Main32 = 6,
        Main41 = 7,
    }
}
