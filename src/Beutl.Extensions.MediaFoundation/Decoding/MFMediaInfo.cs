using System.Text;
using Vortice.MediaFoundation;
using Vortice.Multimedia;
using Vortice.Win32;
using Windows.Win32.Media.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

internal struct MFMediaInfo
{
    public int VideoStreamIndex; // -1 で映像は存在しない
    public int AudioStreamIndex; // -1 で音声は存在しない

    public MFRatio Fps;
    public long HnsDuration;    // 100ns units
    public int TotalFrameCount;
    public BitmapInfoHeader ImageFormat;
    public int OutImageBufferSize;
    public string VideoFormatName;

    public int TotalAudioSampleCount;
    public WaveFormat AudioFormat;

    // Color-space metadata extracted from the input stream's IMFMediaType.
    // TransferFunction is the only HDR signal we trust — primaries/matrix default to
    // Unknown when the container omits the tag, which is common for SDR files.
    public VideoTransferFunction TransferFunction;
    public VideoPrimaries ColorPrimaries;
    public VideoTransferMatrix YCbCrMatrix;
    public bool IsHdr;

    // The subtype we asked Source Reader to emit (YUY2 for SDR, P010 for HDR).
    // Decoders downstream use this to pick the right YUV→RGB conversion.
    public Guid OutputSubType;

    public readonly string GetMediaInfoText()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Play time: {TimeSpan.FromTicks(HnsDuration)}");

        if (VideoStreamIndex != -1)
        {
            double fps = (double)Fps.Numerator / Fps.Denominator;
            sb.AppendLine($"VideoStreamIndex: {VideoStreamIndex}");
            sb.AppendLine($"  Fps: {fps}");
            sb.AppendLine($"  TotalFrameCount: {TotalFrameCount}");
            sb.AppendLine($"  FrameSize: {ImageFormat.Width}x{ImageFormat.Height}");
            sb.AppendLine($"  TransferFunction: {TransferFunction}");
            sb.AppendLine($"  ColorPrimaries: {ColorPrimaries}");
            sb.AppendLine($"  YCbCrMatrix: {YCbCrMatrix}");
            sb.AppendLine($"  IsHdr: {IsHdr}");
        }
        if (AudioStreamIndex != -1)
        {
            sb.AppendLine($"AudioStreamIndex: {AudioStreamIndex}");
            sb.AppendLine($"  Channels: {AudioFormat.Channels}");
            sb.AppendLine($"  SampleRate: {AudioFormat.SampleRate}");
            sb.AppendLine($"  AverageBytesPerSecond: {AudioFormat.AverageBytesPerSecond}");
            sb.AppendLine($"  BlockAlign: {AudioFormat.BlockAlign}");
            sb.AppendLine($"  BitsPerSample: {AudioFormat.BitsPerSample}");
            sb.AppendLine($"  TotalAudioSampleCount: {TotalAudioSampleCount}");
        }

        return sb.ToString();
    }
};
