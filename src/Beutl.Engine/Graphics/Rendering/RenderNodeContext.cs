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
    /// <c>2 × s_out</c> to bound memory; export leaves it <c>+∞</c>. Applied as the final
    /// <c>min(·, MaxWorkingScale)</c> in <see cref="ResolveWorkingScale"/>. Default <c>+∞</c> = no ceiling.
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
    /// <param name="maxWorkingScale">A global ceiling (FR-037: 2×s_out preview, +∞ export).</param>
    public static float ResolveWorkingScale(
        ReadOnlySpan<EffectiveScale> inputs,
        float outputScale,
        float maxWorkingScale = float.PositiveInfinity)
    {
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
}
