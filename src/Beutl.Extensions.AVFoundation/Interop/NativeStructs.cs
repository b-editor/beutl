using System.Runtime.InteropServices;

namespace Beutl.Extensions.AVFoundation.Interop;

// Struct layouts must match native/BeutlAVF/Sources/CBeutlAVFTypes/include/BeutlAVFTypes.h.

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct BeutlVideoInfo
{
    public int Width;
    public int Height;
    public int CodecFourCC;
    public int FrameRateNum;
    public int FrameRateDen;
    public long DurationNum;
    public long DurationDen;
    public long NominalFrameCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct BeutlAudioInfo
{
    public int SampleRate;
    public int ChannelCount;
    public int CodecFourCC;
    public long DurationNum;
    public long DurationDen;
    public long NominalSampleCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct BeutlReaderOptions
{
    public int MaxVideoBufferSize;
    public int MaxAudioBufferSize;
    public int ThresholdFrameCount;
    public int ThresholdSampleCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct BeutlVideoEncoderConfig
{
    public int Width;
    public int Height;
    public int SourceWidth;
    public int SourceHeight;
    public int Codec;                 // 0=Default, 1=H264, 2=JPEG
    public int Bitrate;               // -1 = unspecified
    public int KeyframeInterval;      // -1 = unspecified
    public int ProfileLevelH264;      // 0=Default
    public int FrameRateNum;
    public int FrameRateDen;
    public float JpegQuality;         // < 0 = unspecified
    public int Padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct BeutlAudioEncoderConfig
{
    public int SampleRate;
    public int ChannelCount;
    public int FormatFourCC;
    public int Bitrate;                    // -1 = unspecified
    public int Quality;                    // -1 = Default
    public int SampleRateConverterQuality;
    public int LinearPcmBitDepth;
    public int LinearPcmFlags;             // bit0=Float, bit1=BigEndian, bit2=NonInterleaved
}
