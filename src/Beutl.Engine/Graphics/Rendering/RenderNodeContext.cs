namespace Beutl.Graphics.Rendering;

public class RenderNodeContext(
    RenderNodeOperation[] input,
    float outputScale = 1f,
    float maxWorkingScale = float.PositiveInfinity)
{
    public RenderNodeOperation[] Input { get; } = input;

    public bool IsRenderCacheEnabled { get; set; } = true;

    /// <summary>
    /// The final render-target scale <c>s_out</c> (device px per logical unit at the root).
    /// Sanitized to positive-finite at construction.
    /// Informational only for intermediates — never clamps working scale.
    /// </summary>
    public float OutputScale { get; } =
        float.IsFinite(outputScale) && outputScale > 0f ? outputScale : 1f;

    /// <summary>
    /// Global working-scale ceiling. Preview caps at <c>2 * s_out</c>; export passes <c>+Inf</c>.
    /// A degenerate value (NaN / non-positive) is treated as <c>+Inf</c>.
    /// </summary>
    public float MaxWorkingScale { get; } = SanitizeMaxWorkingScale(maxWorkingScale);

    /// <summary>
    /// Normalizes a working-scale ceiling: a degenerate value (NaN or non-positive) means "no ceiling" (+Inf).
    /// Public entry points that accept a raw ceiling route it through here so the rule stays in one place.
    /// </summary>
    /// <returns><see cref="float.PositiveInfinity"/> for NaN or non-positive input; otherwise the value unchanged.</returns>
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
}
