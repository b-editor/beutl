using Beutl.Media;

namespace Beutl.Media.Proxy;

public interface IProxyResolver
{
    ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset);

    /// <summary>
    /// A version that starts at 0 and increases only when this source's own proxy entries change.
    /// </summary>
    /// <param name="absolutePath">
    /// A normalized source path key (<see cref="ProxyFingerprint.ResolveComparableKey"/>), since a moved or deleted
    /// original cannot be fingerprinted yet may still gain a proxy.
    /// </param>
    long GetSourceVersion(string absolutePath);

    IDisposable Pin(ProxyResolution resolution);

    /// <summary>
    /// True while a <see cref="Pin"/> is held; on the interface so eviction also honors a custom resolver's pins.
    /// </summary>
    bool IsPinned(string absoluteProxyFilePath);
}

public sealed record ProxyResolution(
    string AbsoluteProxyFilePath,
    ProxyFingerprint Source,
    ProxyPreset Preset,
    PixelSize OriginalLogicalFrameSize,
    PixelSize ProxyDecodedFrameSize)
{
    public float SupplyDensity => SupplyDensityOf(OriginalLogicalFrameSize, ProxyDecodedFrameSize);

    internal static float SupplyDensityOf(PixelSize originalLogicalFrameSize, PixelSize proxyDecodedFrameSize)
    {
        int originalLongEdge = Math.Max(originalLogicalFrameSize.Width, originalLogicalFrameSize.Height);
        int proxyLongEdge = Math.Max(proxyDecodedFrameSize.Width, proxyDecodedFrameSize.Height);
        return originalLongEdge == 0 || proxyLongEdge == 0
            ? 1f
            : (float)proxyLongEdge / originalLongEdge;
    }
}
