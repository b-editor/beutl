using Beutl.Media;

namespace Beutl.Media.Proxy;

public interface IProxyResolver
{
    ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset);

    /// <summary>
    /// Returns a monotonically increasing version for a single source file, bumped
    /// only when that source's own proxy entries change (registered / state-changed /
    /// deleted). Callers cache the value per source and reload when it changes, so a
    /// proxy change to one source never forces unrelated proxied sources to reopen.
    /// </summary>
    /// <param name="sourceAbsolutePath">Absolute path of the original media file.</param>
    long GetSourceVersion(string sourceAbsolutePath);

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
            int proxyLongEdge = Math.Max(ProxyDecodedFrameSize.Width, ProxyDecodedFrameSize.Height);
            return originalLongEdge == 0 || proxyLongEdge == 0
                ? 1f
                : (float)proxyLongEdge / originalLongEdge;
        }
    }
}
