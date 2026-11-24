using Beutl.Configuration;
using Beutl.Media;
using Beutl.Media.Decoding;

namespace Beutl;

/// <summary>
/// Holds the services and configuration used to render and compose a scene.
/// </summary>
/// <param name="Fonts">The font manager used to resolve text fonts.</param>
/// <param name="Decoders">The decoder registry used to open media sources.</param>
/// <param name="EditorConfig">The editor settings used for previews and render caching.</param>
/// <param name="ProxyConfig">The settings used to select media proxies.</param>
public sealed record SceneRenderServices(
    FontManager? Fonts = null,
    DecoderRegistry? Decoders = null,
    EditorConfig? EditorConfig = null,
    ProxyStoreConfig? ProxyConfig = null)
{
    /// <summary>
    /// Gets the shared instance with no services or configuration.
    /// </summary>
    public static SceneRenderServices Empty { get; } = new();

    /// <summary>
    /// Returns services with the same font manager and decoder registry and no configuration.
    /// </summary>
    public SceneRenderServices WithoutConfiguration()
        => EditorConfig is null && ProxyConfig is null
            ? this
            : this with { EditorConfig = null, ProxyConfig = null };
}
