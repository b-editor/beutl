namespace Beutl.Graphics.Rendering;

public class RenderNodeContext(
    RenderNodeOperation[] input,
    float outputScale = 1f,
    float maxWorkingScale = float.PositiveInfinity)
{
    public RenderNodeOperation[] Input { get; } = input;

    public bool IsRenderCacheEnabled { get; set; } = true;

    /// <summary>
    /// The final render-target scale <c>s_out</c> for this pull (feature 003): device px per logical unit
    /// at the root. <c>1.0</c> means logical == device (byte-identical to pre-feature). It is the per-pull
    /// working-scale <b>floor</b> and the final blit density: a denser concrete supply runs above it (never a
    /// ceiling on an intermediate — FR-016), and <see cref="ResolveWorkingScale"/> floors the resolved
    /// <c>w</c> at it so an effect never runs below the deliverable density. The floor is on
    /// <see cref="ResolveWorkingScale"/>'s output, not an end-to-end guarantee: the later per-buffer clamp
    /// <see cref="ClampWorkingScaleToBufferBudget"/> (FR-037(b)) may still drive an over-budget buffer below
    /// <c>s_out</c>. Sanitized to positive-finite at construction so every downstream consumer (effects,
    /// particles, 3D) inherits a safe density without re-validating.
    /// </summary>
    public float OutputScale { get; } =
        float.IsFinite(outputScale) && outputScale > 0f ? outputScale : 1f;

    /// <summary>
    /// The global working-scale ceiling for this pull (feature 003, FR-037): preview caps it at
    /// <c>2 × s_out</c>; export passes <c>+∞</c> (no ceiling) so the delivery render follows the true supply
    /// density. This bounds <c>w</c> itself, not OOM: a single buffer's memory scales <c>area × w²</c> (a
    /// clamped <c>16384²×8 ≈ 2 GiB</c> is still possible). Allocatability is guaranteed per-buffer by
    /// <see cref="ClampWorkingScaleToBufferBudget"/> (16384 px/axis); a cross-buffer aggregate (request-scoped
    /// byte/area allocator) is the complete fix (follow-up). Applied as the final
    /// <c>min(·, MaxWorkingScale)</c> in <see cref="ResolveWorkingScale"/>. The constructor default is
    /// <c>+∞</c> for non-render-request callers; a NaN / non-positive seed is treated as <c>+∞</c> so a
    /// degenerate ceiling can never NaN-propagate into <c>w</c> or pull it to zero.
    /// </summary>
    public float MaxWorkingScale { get; } =
        float.IsNaN(maxWorkingScale) || maxWorkingScale <= 0f ? float.PositiveInfinity : maxWorkingScale;

    public Rect CalculateBounds()
    {
        return Input.Aggregate<RenderNodeOperation, Rect>(default, (current, operation) => current.Union(operation.Bounds));
    }

    /// <summary>
    /// Computes the working scale <c>w</c> for a buffer-allocating boundary from its inputs' supply
    /// densities and the request's output scale (feature 003, FR-036):
    /// <c>w = min( max(s_out, densest concrete supply), maxWorkingScale )</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supply-driven on the high side: a concrete (bitmap) input runs the effect at its own density, so a
    /// 2.0 source feeding a 1.0 timeline stays 2.0 — the output scale is <b>not a ceiling</b> on an
    /// intermediate (FR-016); only <paramref name="maxWorkingScale"/> bounds it from above.
    /// </para>
    /// <para>
    /// The output scale <b>is a floor</b>: vector (<see cref="EffectiveScale.Unbounded"/>) inputs and any
    /// sub-output concrete supply (an enlarged / low-density bitmap, e.g. <c>At(0.5)</c>) are lifted to
    /// <c>s_out</c>. An effect's own working resolution (blur kernel / shadow / shader grid) is distinct from
    /// the source's available detail: running it below <c>s_out</c> only discards resolution the delivery
    /// target can use, without fabricating source detail. So flooring at <c>s_out</c> keeps an effect at the
    /// deliverable density (matching the pre-feature renderer at export) while a denser source still lifts
    /// <c>w</c> above it. A genuine reduced-scale proxy survives the floor: at a <c>0.5</c> preview an
    /// <c>At(0.5)</c> proxy gives <c>max(0.5, 0.5) = 0.5</c>, so reduced-scale preview stays cheap. At
    /// <c>s_out == 1</c> with unit-scale / vector inputs this is <c>max(1, 1) == 1</c>, so the byte-identity
    /// anchor (FR-005 / SC-001) is untouched.
    /// </para>
    /// <para>
    /// An effect needing a different working scale (e.g. clamp-to-output for perf, or oversample for SSAA)
    /// customizes its <see cref="FilterEffectRenderNode"/> via <c>FilterEffect.Resource.CreateRenderNode()</c>
    /// and computes <c>w</c> directly in its <c>Process</c> override — there is intentionally no declarative
    /// per-effect policy knob.
    /// </para>
    /// </remarks>
    /// <param name="inputs">The effective scales of the boundary's input operations.</param>
    /// <param name="outputScale">The render request's output scale <c>s_out</c> (the working-scale floor).</param>
    /// <param name="maxWorkingScale">A global ceiling (FR-037: 2×s_out preview, +∞ export — dimension-clamped).</param>
    public static float ResolveWorkingScale(
        ReadOnlySpan<EffectiveScale> inputs,
        float outputScale,
        float maxWorkingScale = float.PositiveInfinity)
    {
        // A degenerate request scale (0 / negative / NaN / inf) must never become the floor below: it would
        // flow a non-finite or zero density into a zero-sized buffer. Production always passes a positive-finite
        // outputScale; this only hardens the contract against misuse.
        if (!float.IsFinite(outputScale) || outputScale <= 0f)
            outputScale = 1f;

        // w is floored at the deliverable density (s_out) and raised by the densest concrete (bitmap) input.
        // Vector (Unbounded) inputs impose no supply, so an all-vector boundary stays at the floor.
        float supply = outputScale;
        foreach (EffectiveScale e in inputs)
        {
            if (e.IsUnbounded) continue;
            if (e.Value > supply) supply = e.Value;
        }

        return MathF.Min(supply, maxWorkingScale);
    }

    /// <summary>
    /// The largest device-buffer axis (px) any feature-003 intermediate may allocate. A GPU texture larger
    /// than this fails to allocate (Vulkan/Skia max-2D-image limit is commonly 16384).
    /// </summary>
    public const int MaxBufferDimension = 16384;

    /// <summary>
    /// Clamps a working scale so the device buffer it allocates for <paramref name="logicalBounds"/> stays
    /// within <paramref name="maxDimension"/> on each axis (feature 003). The FR-037 ceiling bounds <c>w</c>
    /// but not the buffer: this clamps the per-axis GPU texture limit, which scales with <c>axis × w</c>;
    /// memory, which scales with <c>area × w²</c>, is bounded by nothing here by design. A supply-driven
    /// <c>w</c> — especially an <b>anisotropic</b> transform projected onto its most-detailed axis (FR-019),
    /// inflating the stretched-axis bounds while raising density — can size a buffer past the GPU limit
    /// (un-allocatable → crash) or into the multi-GiB range. This reduces <c>w</c> to fit, trading quality on
    /// the densest axis for an allocatable surface rather than failing. Returns <c>w</c> unchanged when the
    /// buffer already fits (the common case, so non-pathological renders and byte-identity at <c>w == 1</c>
    /// are preserved).
    /// </summary>
    public static float ClampWorkingScaleToBufferBudget(
        Rect logicalBounds, float w, int maxDimension = MaxBufferDimension)
    {
        if (!float.IsFinite(w) || w <= 0f) return w;

        double maxAxis = Math.Max(Math.Abs((double)logicalBounds.Width), Math.Abs((double)logicalBounds.Height));
        // Guard degenerate bounds (NaN/∞ from a degenerate transform's AABB): a non-finite maxAxis makes
        // largestAxisPx non-finite, skips the early-out, and reaches MathF.Min/Max(w, NaN), which propagate
        // NaN — so the clamp would itself return a non-finite working scale. Pass w through unchanged; this
        // function must never be the one to introduce a non-finite density.
        if (!double.IsFinite(maxAxis) || maxAxis <= 0) return w;

        double largestAxisPx = Math.Ceiling(maxAxis * w);
        if (largestAxisPx <= maxDimension || largestAxisPx <= 0) return w;

        // Reduce w so the larger axis lands at maxDimension. The factor is exact in double, but narrowing to
        // float can round up — enough to push ceil(axis × fit) one px past the limit. Step the float factor
        // down until the buffer is provably within the limit (in practice at most one ULP), so the bound is a
        // hard guarantee (<= maxDimension).
        float fit = (float)(w * (maxDimension / largestAxisPx));
        while (fit > 0f && Math.Ceiling(maxAxis * fit) > maxDimension)
            fit = MathF.BitDecrement(fit);
        return MathF.Max(MathF.Min(w, fit), 0f);
    }
}
