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
    public int IsHdr;                 // 0 = SDR, 1 = HDR
    public int TransferFunction;      // BeutlTransferFunction
    public int ColorPrimaries;        // BeutlColorPrimaries
    public int BytesPerPixel;         // 4 (Bgra8888) or 8 (Rgba16161616)
}

internal enum BeutlTransferFunction
{
    Unknown = 0,
    Srgb = 1,
    Linear = 2,
    Bt709 = 3,
    Pq = 4,
    Hlg = 5,
    Rec2020 = 6,
    TwoDotTwo = 7,
    Gamma28 = 8,
    Smpte240M = 9,
    Smpte428 = 10,
}

internal enum BeutlColorPrimaries
{
    Unknown = 0,
    Srgb = 1,
    Bt709 = 2,
    Bt470M = 3,
    Bt470BG = 4,
    Smpte170M = 5,
    Smpte240M = 6,
    Film = 7,
    Rec2020 = 8,
    Xyz = 9,
    Smpte431 = 10,
    Dcip3 = 11,
    Ebu3213 = 12,
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
    public int Codec;                 // 0=Default, 1=H264, 2=JPEG, 3=HEVC
    public int Bitrate;               // -1 = unspecified
    public int KeyframeInterval;      // -1 = unspecified
    public int ProfileLevelH264;      // 0=Default
    public int FrameRateNum;
    public int FrameRateDen;
    public float JpegQuality;         // < 0 = unspecified
    public int IsHdr;                 // 0 = SDR, 1 = HDR
    public int ColorTransfer;         // BeutlTransferFunction
    public int ColorPrimaries;        // BeutlColorPrimaries
    public int YCbCrMatrix;           // BeutlYCbCrMatrix
}

internal enum BeutlYCbCrMatrix
{
    Unknown = 0,
    Bt709 = 1,
    Bt601 = 2,
    Rec2020 = 3,
    Smpte240M = 4,
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
