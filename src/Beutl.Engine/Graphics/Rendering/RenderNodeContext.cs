namespace Beutl.Graphics.Rendering;

public class RenderNodeContext(
    RenderNodeOperation[] input,
    float outputScale = 1f,
    float maxWorkingScale = float.PositiveInfinity)
{
    public RenderNodeOperation[] Input { get; } = input;

    public bool IsRenderCacheEnabled { get; set; } = true;

    /// <summary>
    /// The final render-target scale <c>s_out</c> for this pull (feature 003): device pixels per
    /// logical unit at the root. <c>1.0</c> means logical == device (byte-identical to pre-feature).
    /// It is the final target only — never a ceiling on an intermediate boundary's working scale.
    /// </summary>
    public float OutputScale { get; } = outputScale;

    /// <summary>
    /// The global working-scale ceiling for this pull (feature 003, FR-037): preview caps it at
    /// <c>2 × s_out</c> to bound interactive working scale; export passes a generous-but-finite
    /// <c>max(8, 4 × s_out)</c> (high enough never to clip a legitimate high-resolution source, yet finite to
    /// stay OOM-safe on long renders). Applied as the final <c>min(·, MaxWorkingScale)</c> in
    /// <see cref="ResolveWorkingScale"/>. The constructor default is <c>+∞</c> (no ceiling) for non-render-request
    /// callers; production render requests always seed a finite value.
    /// </summary>
    public float MaxWorkingScale { get; } = maxWorkingScale;

    public Rect CalculateBounds()
    {
        return Input.Aggregate<RenderNodeOperation, Rect>(default, (current, operation) => current.Union(operation.Bounds));
    }

    /// <summary>
    /// Computes the working scale <c>w</c> for a buffer-allocating boundary from its inputs' supply
    /// densities and the request's output scale (feature 003, FR-036). Supply-driven: a concrete
    /// (bitmap) input imposes its density; vector (<see cref="EffectiveScale.Unbounded"/>) inputs
    /// impose none and rasterize at the output density. The output scale is NOT a ceiling on an
    /// intermediate's working scale — only the global memory ceiling
    /// (<paramref name="maxWorkingScale"/>) bounds it.
    /// </summary>
    /// <remarks>
    /// An effect that needs a working scale other than the supply density (e.g. clamp-to-output for
    /// perf, or oversample for SSAA) customizes its <see cref="FilterEffectRenderNode"/> via
    /// <c>FilterEffect.Resource.CreateRenderNode()</c> and computes <c>w</c> directly in its
    /// <c>Process</c> override — there is intentionally no declarative per-effect policy knob.
    /// </remarks>
    /// <param name="inputs">The effective scales of the boundary's input operations.</param>
    /// <param name="outputScale">The render request's output scale <c>s_out</c>.</param>
    /// <param name="maxWorkingScale">A global ceiling (FR-037: 2×s_out preview, max(8, 4×s_out) export).</param>
    public static float ResolveWorkingScale(
        ReadOnlySpan<EffectiveScale> inputs,
        float outputScale,
        float maxWorkingScale = float.PositiveInfinity)
    {
        // Defensive: a degenerate request scale (0 / negative / NaN / inf) must never reach the all-vector
        // path where supply = outputScale would flow a non-finite or zero density into a zero-sized buffer.
        // Production always passes a clamped, positive-finite outputScale (RenderScale.ToFloat / 1f); this only
        // hardens the contract against misuse — mirroring the validation in At / ClampWorkingScaleToBufferBudget.
        if (!float.IsFinite(outputScale) || outputScale <= 0f)
            outputScale = 1f;

        // supply = the densest concrete (bitmap) input. Unbounded (vector) inputs impose no supply
        // on their own, but their presence alongside a bitmap raises the floor to the output density
        // (see the mixed-content rule below).
        float supply = 0f;
        bool hasConcrete = false;
        bool hasVector = false;
        foreach (EffectiveScale e in inputs)
        {
            if (e.IsUnbounded)
            {
                hasVector = true;
                continue;
            }

            hasConcrete = true;
            if (e.Value > supply) supply = e.Value;
        }

        if (!hasConcrete)
        {
            // No concrete supply => purely vector content; rasterize at the output density.
            supply = outputScale;
        }
        else if (hasVector && outputScale > supply)
        {
            // Mixed bitmap + vector (C4/C5): a low-density bitmap must NOT drag re-rasterizable vector
            // content (crisp text/shapes) down to its density. The vector half can always be drawn at the
            // output density, so the floor is at least s_out — the bitmap's lower density only bounds itself,
            // not the vector siblings. At s_out == 1 with a unit-scale bitmap this is max(1, 1) == 1, so the
            // byte-identity anchor is untouched; it only lifts genuinely-mixed, sub-output cases.
            supply = outputScale;
        }

        // Supply-driven (the former "Inherit" default, now the only behaviour): run at the supply
        // density, bounded only by the global memory ceiling. s_out is not a ceiling here.
        return MathF.Min(supply, maxWorkingScale);
    }

    /// <summary>
    /// The largest device-buffer axis (px) any feature-003 intermediate may allocate. A GPU texture larger
    /// than this fails to allocate (Vulkan/Skia max-2D-image limit is commonly 16384), and even below it the
    /// memory grows with the buffer AREA, not the working scale.
    /// </summary>
    public const int MaxBufferDimension = 16384;

    /// <summary>
    /// Clamps a working scale so the device buffer it allocates for <paramref name="logicalBounds"/> stays
    /// within <paramref name="maxDimension"/> on each axis (feature 003 — the FR-037 ceiling bounds <c>w</c>
    /// but NOT the buffer: the per-axis GPU texture limit scales with <c>axis × w</c> — what this clamp bounds —
    /// while memory scales with <c>area × w²</c>, which nothing bounds here by design).
    /// A supply-driven <c>w</c> — especially an <b>anisotropic</b> transform projected onto its most-detailed
    /// axis (FR-019), which inflates the bounds on the stretched axis while raising the density — can size a
    /// buffer past the GPU limit (un-allocatable → crash) or into the multi-GiB range. This reduces <c>w</c>
    /// to fit, trading a quality reduction on the densest axis for a bounded, allocatable surface, rather than
    /// failing. Returns <c>w</c> unchanged when the buffer already fits (the common case, so non-pathological
    /// renders are untouched and byte-identity at <c>w == 1</c> is preserved).
    /// </summary>
    public static float ClampWorkingScaleToBufferBudget(
        Rect logicalBounds, float w, int maxDimension = MaxBufferDimension)
    {
        if (!float.IsFinite(w) || w <= 0f) return w;

        double maxAxis = Math.Max(Math.Abs((double)logicalBounds.Width), Math.Abs((double)logicalBounds.Height));
        double largestAxisPx = Math.Ceiling(maxAxis * w);
        if (largestAxisPx <= maxDimension || largestAxisPx <= 0) return w;

        // Reduce w so the larger axis lands at maxDimension. The factor is exact in double, but narrowing it to
        // float can round UP — enough to push ceil(axis × fit) one px past the limit. Step the float factor down
        // until the resulting buffer is provably within the limit (in practice at most one ULP), so the bound is
        // a hard guarantee (<= maxDimension), not "a hair of margin".
        float fit = (float)(w * (maxDimension / largestAxisPx));
        while (fit > 0f && Math.Ceiling(maxAxis * fit) > maxDimension)
            fit = MathF.BitDecrement(fit);
        return MathF.Max(MathF.Min(w, fit), 0f);
    }
}
