using Beutl.Media;

namespace Beutl.Media.Proxy;

public interface IProxyResolver
{
    long Version { get; }

    ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset);

    IDisposable Pin(ProxyResolution resolution);
}

public sealed record ProxyResolution(
    string AbsoluteProxyFilePath,
    ProxyFingerprint Source,
    ProxyPreset Preset,
    PixelSize OriginalLogicalFrameSize,
    PixelSize ProxyDecodedFrameSize)
{
    public float SupplyDensity
    {
        get
        {
            int originalLongEdge = Math.Max(OriginalLogicalFrameSize.Width, OriginalLogicalFrameSize.Height);
            return originalLongEdge == 0
                ? 1f
                : (float)Math.Max(ProxyDecodedFrameSize.Width, ProxyDecodedFrameSize.Height) / originalLongEdge;
        }
    }
}
