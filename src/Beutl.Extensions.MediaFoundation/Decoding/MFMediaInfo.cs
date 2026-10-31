using System.Text;
using Beutl.Media;
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

    public MFRatio Fps;
    public long HnsDuration;    // 100ns units
    public int TotalFrameCount;
    public BitmapInfoHeader ImageFormat;
    public int OutImageBufferSize;
    public string VideoFormatName;

    // The stream's color tags; untagged streams keep the BT.601 limited-range sRGB reading.
    public VideoColorTags Color;


    public readonly string GetMediaInfoText()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Play time: {TimeSpan.FromTicks(HnsDuration)}");

        double fps = (double)Fps.Numerator / Fps.Denominator;
        sb.AppendLine($"VideoStreamIndex: {VideoStreamIndex}");
        sb.AppendLine($"  Fps: {fps}");
        sb.AppendLine($"  TotalFrameCount: {TotalFrameCount}");
        sb.AppendLine($"  FrameSize: {ImageFormat.Width}x{ImageFormat.Height}");

        return sb.ToString();
    }
};
