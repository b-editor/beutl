using MonoMac.AudioToolbox;
using MonoMac.AVFoundation;

namespace Beutl.Extensions.AVFoundation.Encoding;

public static class EnumMapper
{
    public static AVVideoCodec? ToAVVideoCodec(this AVFVideoEncoderSettings.VideoCodec codec)
    {
        return codec switch
        {
            AVFVideoEncoderSettings.VideoCodec.H264 => AVVideoCodec.H264,
            AVFVideoEncoderSettings.VideoCodec.JPEG => AVVideoCodec.JPEG,
            _ => null
        };
    }

    public static AVVideoProfileLevelH264? ToAVVideoProfileLevelH264(this AVFVideoEncoderSettings.VideoProfileLevelH264 profile)
    {
        return profile switch
        {
            AVFVideoEncoderSettings.VideoProfileLevelH264.Baseline30 => AVVideoProfileLevelH264.Baseline30,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Baseline31 => AVVideoProfileLevelH264.Baseline31,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Baseline41 => AVVideoProfileLevelH264.Baseline41,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Main30 => AVVideoProfileLevelH264.Main30,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Main31 => AVVideoProfileLevelH264.Main31,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Main32 => AVVideoProfileLevelH264.Main32,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Main41 => AVVideoProfileLevelH264.Main41,
            _ => null
        };
    }

    public static AudioFormatType? ToAudioFormatType(this AVFAudioEncoderSettings.AudioFormatType format)
    {
        return format switch
        {
            AVFAudioEncoderSettings.AudioFormatType.MPEGLayer1 => AudioFormatType.MPEGLayer1,
            AVFAudioEncoderSettings.AudioFormatType.MPEGLayer2 => AudioFormatType.MPEGLayer2,
            AVFAudioEncoderSettings.AudioFormatType.MPEGLayer3 => AudioFormatType.MPEGLayer3,
            AVFAudioEncoderSettings.AudioFormatType.Audible => AudioFormatType.Audible,
            AVFAudioEncoderSettings.AudioFormatType.MACE3 => AudioFormatType.MACE3,
            AVFAudioEncoderSettings.AudioFormatType.MACE6 => AudioFormatType.MACE6,
            AVFAudioEncoderSettings.AudioFormatType.QDesign2 => AudioFormatType.QDesign2,
            AVFAudioEncoderSettings.AudioFormatType.QDesign => AudioFormatType.QDesign,
            AVFAudioEncoderSettings.AudioFormatType.QUALCOMM => AudioFormatType.QUALCOMM,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC => AudioFormatType.MPEG4AAC,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_ELD => AudioFormatType.MPEG4AAC_ELD,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_ELD_SBR => AudioFormatType.MPEG4AAC_ELD_SBR,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_ELD_V2 => AudioFormatType.MPEG4AAC_ELD_V2,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_HE => AudioFormatType.MPEG4AAC_HE,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_LD => AudioFormatType.MPEG4AAC_LD,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_HE_V2 => AudioFormatType.MPEG4AAC_HE_V2,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_Spatial => AudioFormatType.MPEG4AAC_Spatial,
            AVFAudioEncoderSettings.AudioFormatType.AC3 => AudioFormatType.AC3,
            AVFAudioEncoderSettings.AudioFormatType.AES3 => AudioFormatType.AES3,
            AVFAudioEncoderSettings.AudioFormatType.AppleLossless => AudioFormatType.AppleLossless,
            AVFAudioEncoderSettings.AudioFormatType.ALaw => AudioFormatType.ALaw,
            AVFAudioEncoderSettings.AudioFormatType.ParameterValueStream => AudioFormatType.ParameterValueStream,
            AVFAudioEncoderSettings.AudioFormatType.CAC3 => AudioFormatType.CAC3,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4CELP => AudioFormatType.MPEG4CELP,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4HVXC => AudioFormatType.MPEG4HVXC,
            AVFAudioEncoderSettings.AudioFormatType.iLBC => AudioFormatType.iLBC,
            AVFAudioEncoderSettings.AudioFormatType.AppleIMA4 => AudioFormatType.AppleIMA4,
            AVFAudioEncoderSettings.AudioFormatType.LinearPCM => AudioFormatType.LinearPCM,
            AVFAudioEncoderSettings.AudioFormatType.MIDIStream => AudioFormatType.MIDIStream,
            AVFAudioEncoderSettings.AudioFormatType.DVIIntelIMA => AudioFormatType.DVIIntelIMA,
            AVFAudioEncoderSettings.AudioFormatType.MicrosoftGSM => AudioFormatType.MicrosoftGSM,
            AVFAudioEncoderSettings.AudioFormatType.AMR => AudioFormatType.AMR,
            AVFAudioEncoderSettings.AudioFormatType.TimeCode => AudioFormatType.TimeCode,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4TwinVQ => AudioFormatType.MPEG4TwinVQ,
            AVFAudioEncoderSettings.AudioFormatType.ULaw => AudioFormatType.ULaw,
            _ => null
        };
    }
}
