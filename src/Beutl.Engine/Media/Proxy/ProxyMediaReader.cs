using System.Diagnostics.CodeAnalysis;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Source;

namespace Beutl.Media.Proxy;

internal sealed class ProxyMediaReader(
    MediaReader inner,
    IDisposable pin,
    ProxyResolution resolution) : MediaReader
{
    public override VideoStreamInfo VideoInfo => inner.VideoInfo;

    public override AudioStreamInfo AudioInfo => inner.AudioInfo;

    public override bool HasVideo => inner.HasVideo;

    public override bool HasAudio => inner.HasAudio;

    public override ProxyResolution? ProxyResolution => resolution;

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        return inner.ReadVideo(frame, out image);
    }

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        return inner.ReadAudio(start, length, out sound);
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
                inner.Dispose();
        }
        finally
        {
            // Released on the finalizer path too, or an abandoned reader pins the proxy against
            // eviction forever. PinHandle touches only Interlocked/ConcurrentDictionary state, so
            // it is finalizer-safe despite being a managed object.
            pin.Dispose();
        }

        base.Dispose(disposing);
    }
}
