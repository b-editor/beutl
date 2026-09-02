using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

/// <summary>Describes one complete request issued through a <see cref="RenderNodeRenderer"/>.</summary>
public sealed record RenderNodeRenderRequest
{
    /// <summary>Gets the intent that selects allocation-failure behavior.</summary>
    /// <remarks>
    /// Stated rather than defaulted: <see cref="RenderNodeRenderer.Rasterize"/> has no destination canvas to
    /// promote a delivery intent from, so an implicit <see cref="RenderIntent.Preview"/> would silently give a
    /// delivery caller a frame whose unallocatable intermediates were dropped instead of reported.
    /// </remarks>
    public required RenderIntent Intent { get; init; }

    /// <summary>Gets the optional finite logical domain for target-less root target accesses.</summary>
    /// <remarks>
    /// A non-null value must be finite and non-empty. It is used by target-less renderer operations when a
    /// root fragment requires a target domain. Rendering into a supplied canvas uses its destination viewport
    /// instead. <see langword="null"/> is valid for self-bounded graphs that do not require a root
    /// <see cref="TargetRegion.Full"/> access.
    /// </remarks>
    public Rect? TargetDomain { get; init; }

    /// <summary>Gets the optional final logical output region requested by the caller.</summary>
    /// <remarks>
    /// <see langword="null"/> selects the complete conservative output extent. A finite empty rectangle is a
    /// successful empty request. This property does not provide or shrink <see cref="TargetDomain"/>.
    /// </remarks>
    public Rect? RequestedRegion { get; init; }

    /// <summary>Gets the requested device-pixel density for target-less rasterization and metadata queries.</summary>
    /// <remarks>
    /// Non-finite and non-positive values are sanitized to <c>1</c>. Rendering into a supplied canvas uses the
    /// destination density instead.
    /// </remarks>
    public float OutputScale { get; init; } = 1;

    /// <summary>Gets the maximum working density allowed for intermediate values.</summary>
    /// <remarks>
    /// NaN and non-positive values are sanitized to positive infinity. Positive finite values and positive
    /// infinity are preserved.
    /// </remarks>
    public float MaxWorkingScale { get; init; } = float.PositiveInfinity;

    /// <summary>Gets the persistent render-node cache admission policy for this request.</summary>
    public RenderCacheOptions CacheOptions { get; init; } = RenderCacheOptions.Default;

    /// <summary>Gets the execution purpose observed by render callbacks and cache policy.</summary>
    /// <remarks>
    /// <see cref="RenderNodeRenderer.Render"/> and <see cref="RenderNodeRenderer.Rasterize"/> preserve this value.
    /// Metadata-only measurement and hit-testing use their dedicated engine purposes.
    /// </remarks>
    public RenderRequestPurpose Purpose { get; init; } = RenderRequestPurpose.Auxiliary;

    internal FusionMode FusionMode { get; init; } = FusionMode.Enabled;
}
