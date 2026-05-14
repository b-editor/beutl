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
    // TransferFunction is the primary HDR signal we trust — primaries/matrix default
    // to Unknown when the container omits the tag, which is common for SDR files.
    // Note: a stream that tags only ColorPrimaries (e.g. Rec.2020) without a
    // transfer function still reports IsHdr=false here, so HDR-by-primaries-only
    // sources fall through the SDR path. That mirrors the FFmpeg/AVF backends.
    public VideoTransferFunction TransferFunction;
    public VideoPrimaries ColorPrimaries;
    public VideoTransferMatrix YCbCrMatrix;
    public bool IsHdr;

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
