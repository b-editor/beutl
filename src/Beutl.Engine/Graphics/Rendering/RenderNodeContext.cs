namespace Beutl.Graphics.Rendering;

public class RenderNodeContext(
    RenderNodeOperation[] input,
    RenderIntent renderIntent,
    float outputScale = 1f,
    float maxWorkingScale = float.PositiveInfinity,
    RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
{
    public RenderNodeOperation[] Input { get; } = input;

    public bool IsRenderCacheEnabled { get; set; } = true;

    /// <summary>
    /// Identifies a frame pull versus auxiliary work such as hit-testing or boundary calculation.
    /// Auxiliary pulls may execute nodes but must not replace frame-render cache state with their different ROI.
    /// </summary>
    public RenderPullPurpose PullPurpose { get; } =
        RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));

    /// <summary>True when this pull must preserve retained frame-render state.</summary>
    internal bool IsAuxiliaryPull => PullPurpose == RenderPullPurpose.Auxiliary;

    /// <summary>
    /// The owning renderer's effect-pipeline counters, seeded by <see cref="RenderNodeProcessor"/>. The property
    /// itself defaults to <see langword="null"/>, but the processor-driven pull path always seeds an instance
    /// (<see cref="RenderNodeProcessor"/> fabricates one when none is passed in), so effect nodes see it non-null
    /// there. It stays <see langword="null"/> only when a context is constructed directly without one; effect nodes
    /// then skip counting after a single null check (zero overhead).
    /// </summary>
    public PipelineDiagnostics? Diagnostics { get; internal set; }

    /// <summary>
    /// The owning renderer's render-target pool, seeded by <see cref="RenderNodeProcessor"/>.
    /// <see langword="null"/> when the pull path was given no pool; effect intermediates then allocate
    /// directly via <see cref="RenderTarget.Create"/>, behavior-identical to the pre-pool pipeline.
    /// </summary>
    internal RenderTargetPool? Pool { get; set; }

    /// <summary>
    /// The owning processor's render-target allocation capability. It stays internal so nested processing can inherit
    /// custom allocation without exposing renderer-owned pool state through the public authoring surface.
    /// </summary>
    internal Func<int, int, RenderTarget?>? RenderTargetFactory { get; set; }

    /// <summary>
    /// Overrides the render-tree stability predicate when an executor supplies the input operations through an
    /// opaque seam instead of container children. A false value keeps content-blind prefix signatures from treating
    /// same-bounds animated input as stable.
    /// </summary>
    internal bool? InputSubtreeStableOverride { get; init; }

    /// <summary>
    /// Logical region the parent needs from this node's output. <see cref="Rect.Invalid"/> requests the full
    /// output. Filter-effect plans seed their backward ROI walk from this value (FR-011).
    /// </summary>
    public Rect RequestedBounds { get; init; } = Rect.Invalid;

    /// <summary>
    /// The final render-target scale <c>s_out</c> (device px per logical unit at the root).
    /// Sanitized to positive-finite at construction.
    /// Informational only for intermediates — never clamps working scale.
    /// </summary>
    public float OutputScale { get; } =
        float.IsFinite(outputScale) && outputScale > 0f ? outputScale : 1f;

    /// <summary>
    /// Global working-scale ceiling. Interactive preview commonly caps at <c>2 * s_out</c>; export commonly passes <c>+Inf</c>.
    /// Failure policy is carried independently by <see cref="RenderIntent"/>.
    /// A degenerate value (NaN / non-positive) is treated as <c>+Inf</c>.
    /// </summary>
    public float MaxWorkingScale { get; } = SanitizeMaxWorkingScale(maxWorkingScale);

    /// <summary>Explicit preview/delivery failure policy, independent of the working-scale ceiling.</summary>
    public RenderIntent RenderIntent { get; } =
        RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));

    /// <summary>
    /// Creates a processor for a nested render-node tree while inheriting this pull's scale, diagnostics,
    /// render policy, purpose, and executor-owned target pool. The pool remains opaque to the caller.
    /// </summary>
    /// <param name="root">The nested tree root.</param>
    /// <param name="useRenderCache">Whether the nested tree may consume and populate render caches.</param>
    /// <param name="requestedBounds">
    /// The logical output region requested from the nested tree, or <see langword="null"/> to inherit
    /// <see cref="RequestedBounds"/>.
    /// </param>
    public RenderNodeProcessor CreateChildProcessor(
        RenderNode root,
        bool useRenderCache,
        Rect? requestedBounds = null)
    {
        return new RenderNodeProcessor(
            Pool, root, useRenderCache, RenderIntent, OutputScale, MaxWorkingScale, Diagnostics, PullPurpose,
            RenderTargetFactory)
        {
            InputSubtreeStableOverride = InputSubtreeStableOverride,
            RequestedBounds = requestedBounds ?? RequestedBounds,
        };
    }

    /// <summary>Canonical ceiling rule: a degenerate value (NaN or non-positive) means "no ceiling" (+Inf); other values pass through.</summary>
    public static float SanitizeMaxWorkingScale(float maxWorkingScale) =>
        float.IsNaN(maxWorkingScale) || maxWorkingScale <= 0f ? float.PositiveInfinity : maxWorkingScale;

    public Rect CalculateBounds()
    {
        return Input.Aggregate<RenderNodeOperation, Rect>(default, (current, operation) => current.Union(operation.Bounds));
    }

    /// <summary>
    /// Computes the working scale <c>w</c> for a buffer-allocating boundary:
    /// <c>w = min( max(s_out, densest concrete supply), maxWorkingScale )</c>.
    /// Vector inputs are excluded from the supply max; the output scale is a floor, not a ceiling.
    /// </summary>
    public static float ResolveWorkingScale(
        ReadOnlySpan<EffectiveScale> inputs,
        float outputScale,
        float maxWorkingScale = float.PositiveInfinity)
    {
        if (!float.IsFinite(outputScale) || outputScale <= 0f)
            outputScale = 1f;

        float supply = outputScale;
        foreach (EffectiveScale e in inputs)
        {
            if (e.IsUnbounded) continue;
            if (e.Value > supply) supply = e.Value;
        }

        return MathF.Min(supply, SanitizeMaxWorkingScale(maxWorkingScale));
    }

    /// <summary>Max device-buffer axis (px). GPU textures larger than this typically fail to allocate.</summary>
    public const int MaxBufferDimension = 16384;

    /// <summary>
    /// Clamps a working scale so the device buffer for <paramref name="logicalBounds"/> stays within
    /// <paramref name="maxDimension"/> on each axis. Returns <c>w</c> unchanged when the buffer already fits.
    /// Distinct from <see cref="MaxWorkingScale"/> (quality ceiling); this is a per-buffer size guard.
    /// </summary>
    public static float ClampWorkingScaleToBufferBudget(
        Rect logicalBounds, float w, int maxDimension = MaxBufferDimension)
    {
        if (!float.IsFinite(w) || w <= 0f) return w;

        double maxAxis = Math.Max(Math.Abs((double)logicalBounds.Width), Math.Abs((double)logicalBounds.Height));
        // Degenerate bounds (NaN/Inf) must not introduce a non-finite density.
        if (!double.IsFinite(maxAxis) || maxAxis <= 0) return w;

        double largestAxisPx = Math.Ceiling(maxAxis * w);
        if (largestAxisPx <= maxDimension || largestAxisPx <= 0) return w;

        // Step the float factor down until ceil(axis * fit) <= maxDimension (at most one ULP).
        float fit = (float)(w * (maxDimension / largestAxisPx));
        while (fit > 0f && Math.Ceiling(maxAxis * fit) > maxDimension)
            fit = MathF.BitDecrement(fit);
        return MathF.Max(MathF.Min(w, fit), 0f);
    }

    /// <summary>
    /// Device-buffer dimensions for a logical <paramref name="bounds"/> at density <paramref name="w"/>. The single
    /// source of truth for effect-pass buffer sizing so shader resolution uniforms match the allocated buffer.
    /// </summary>
    public static (int Width, int Height) DeviceBufferSize(Rect bounds, float w)
    {
        int bw = w == 1f ? (int)bounds.Width : (int)MathF.Ceiling(bounds.Width * w);
        int bh = w == 1f ? (int)bounds.Height : (int)MathF.Ceiling(bounds.Height * w);
        return (bw, bh);
    }
}
