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
    /// The first observed value for a source with no recorded changes is <c>0</c>;
    /// subsequent bumps yield <c>1, 2, 3, …</c>.
    /// </summary>
    /// <param name="source">
    /// Fingerprint of the original media file. Keying on <see cref="ProxyFingerprint"/>
    /// (whose <see cref="ProxyFingerprint.AbsolutePath"/> is already symlink-resolved and
    /// normalized) keeps the resolver's path rules internal, so callers never have to
    /// reproduce them on a raw path.
    /// </param>
    long GetSourceVersion(ProxyFingerprint source);

    /// <summary>
    /// Path-keyed form of <see cref="GetSourceVersion(ProxyFingerprint)"/> for an original that can no
    /// longer be fingerprinted (moved / deleted) but may still gain a proxy while a reader is open.
    /// <paramref name="absolutePath"/> is the normalized source key — a
    /// <see cref="ProxyFingerprint.AbsolutePath"/> or <see cref="ProxyFingerprint.ResolveComparableKey"/>
    /// result. The default forwards to the fingerprint overload, which keys on the same
    /// <see cref="ProxyFingerprint.AbsolutePath"/>, so existing resolvers need no change; the built-in
    /// resolver looks the path up directly.
    /// </summary>
    long GetSourceVersion(string absolutePath)
        => GetSourceVersion(new ProxyFingerprint { AbsolutePath = absolutePath });

    IDisposable Pin(ProxyResolution resolution);

    /// <summary>
    /// True while at least one transient decode-lifetime safety pin (see <see cref="Pin"/>) is held.
    /// On the interface so an <c>IProxyResolver</c>-dependent eviction service honors a custom
    /// resolver's pins — otherwise swapping <c>DecoderRegistry.ProxyResolver</c> would leave a
    /// custom resolver's proxies unprotected.
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
