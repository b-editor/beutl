namespace Beutl.Graphics.Rendering;

/// <summary>Configures renderer-lifetime ownership and the request used when an operation omits one.</summary>
public sealed class RenderNodeRendererOptions
{
    /// <summary>Gets the complete default request copied and sanitized for the renderer lifetime.</summary>
    /// <remarks>Stated rather than synthesized, so the renderer never invents a request the caller did not write.</remarks>
    public required RenderNodeRenderRequest DefaultRequest { get; init; }

    /// <summary>Gets the optional caller-owned factory for renderer-owned intermediate targets.</summary>
    /// <remarks><see langword="null"/> selects the engine's current-backend RGBA16F allocator.</remarks>
    public IRenderTargetFactory? TargetFactory { get; init; }
}
