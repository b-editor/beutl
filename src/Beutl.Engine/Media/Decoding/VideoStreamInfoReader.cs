using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.Media.Decoding;

// Adapts metadata from older decoder plugins without changing their read and lifetime behavior.
internal sealed class VideoStreamInfoReader(MediaReader reader, VideoStreamInfo videoInfo) : MediaReader
{
    public override VideoStreamInfo VideoInfo => videoInfo;
    public override AudioStreamInfo AudioInfo => reader.AudioInfo;
    public override bool HasVideo => reader.HasVideo;
    public override bool HasAudio => reader.HasAudio;
    public override ProxyResolution? ProxyResolution => reader.ProxyResolution;

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        => reader.ReadVideo(frame, out image);

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        => reader.ReadAudio(start, length, out sound);

    protected override void Dispose(bool disposing)
    {
        if (disposing) reader.Dispose();
        base.Dispose(disposing);
    }
}
