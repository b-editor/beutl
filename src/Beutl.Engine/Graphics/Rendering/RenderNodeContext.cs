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
    /// densities, the request's output scale, and the boundary's <see cref="ResolutionPolicy"/>
    /// (feature 003, FR-036). Supply-driven: a concrete (bitmap) input imposes its density; vector
    /// (<see cref="EffectiveScale.Unbounded"/>) inputs impose none and rasterize at the output density.
    /// </summary>
    /// <param name="inputs">The effective scales of the boundary's input operations.</param>
    /// <param name="outputScale">The render request's output scale <c>s_out</c>.</param>
    /// <param name="policy">The boundary's resolution policy.</param>
    /// <param name="maxWorkingScale">A global ceiling (FR-037: 2×s_out preview, +∞ export).</param>
    public static float ResolveWorkingScale(
        ReadOnlySpan<EffectiveScale> inputs,
        float outputScale,
        ResolutionPolicy policy,
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

        float w = policy.Kind switch
        {
            // Perf opt-out: clamp the working scale down to the output scale.
            ResolutionPolicyKind.ClampToOutput => MathF.Min(supply, outputScale),
            // Quality opt-in: at least Factor×output, even from a lower-density input (SSAA on demand).
            // Factor is positive by construction (ResolutionPolicy's ctor rejects a non-positive Oversample
            // factor); the Factor <= 0 arm below stays only as a defensive degrade-to-supply (== Inherit),
            // so a hypothetical zero factor never amplifies (F3).
            ResolutionPolicyKind.Oversample when policy.Factor > 0f
                => MathF.Max(supply, policy.Factor * outputScale),
            ResolutionPolicyKind.Oversample => supply,
            // Inherit (default): run at the supply density. The output scale is not a ceiling.
            _ => supply,
        };

        // An EXPLICIT Oversample(factor) request must reach factor×s_out even under a global ceiling (C8):
        // the preview cap (FR-037: 2×s_out) exists to bound *passive* memory growth, not to silently neuter a
        // deliberate quality opt-in — otherwise Oversample(2)/(4)/(8) all collapse to the same 2×s_out preview.
        // The escape raises the ceiling only to the requested factor target; supply above that is still the
        // passive part and stays bounded by maxWorkingScale. Inherit/Clamp keep the plain ceiling, so the
        // byte-identity anchor (default policy, w == 1) is untouched.
        float ceiling = policy.Kind == ResolutionPolicyKind.Oversample && policy.Factor > 0f
            ? MathF.Max(maxWorkingScale, policy.Factor * outputScale)
            : maxWorkingScale;

        return MathF.Min(w, ceiling);
    }
}
