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
    /// <param name="source">
    /// Fingerprint of the original media file. Keying on <see cref="ProxyFingerprint"/>
    /// (whose <see cref="ProxyFingerprint.AbsolutePath"/> is already symlink-resolved and
    /// normalized) keeps the resolver's path rules internal, so callers never have to
    /// reproduce them on a raw path.
    /// </param>
    long GetSourceVersion(ProxyFingerprint source);

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
