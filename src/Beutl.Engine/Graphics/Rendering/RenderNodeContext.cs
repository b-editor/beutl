namespace Beutl.Graphics.Rendering;

public class RenderNodeContext(RenderNodeOperation[] input, float outputScale = 1f)
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
    /// A per-pull floor propagated downward by an ancestor <see cref="ResolutionPolicyKind.PreserveSource"/>
    /// so a descendant boundary keeps a high source's density even under a clamp. <c>0</c> = no floor.
    /// </summary>
    /// <remarks>
    /// Each <see cref="RenderNodeProcessor.Pull"/> creates a fresh context per node, so writing this
    /// setter does NOT automatically reach descendant contexts — downward propagation is the puller's
    /// responsibility (wired in when live <see cref="ResolutionPolicyKind.PreserveSource"/> lands, FR-036).
    /// In Slice 0 it is inert (<c>w = 1</c> everywhere).
    /// </remarks>
    public float PreserveFloor { get; set; }

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
    /// <param name="preserveFloor">A minimum working scale forced by an ancestor PreserveSource (0 = none).</param>
    /// <param name="maxWorkingScale">A global ceiling (FR-037: 2×s_out preview, +∞ export).</param>
    public static float ResolveWorkingScale(
        ReadOnlySpan<EffectiveScale> inputs,
        float outputScale,
        ResolutionPolicy policy,
        float preserveFloor = 0f,
        float maxWorkingScale = float.PositiveInfinity)
    {
        // supply = the densest concrete (bitmap) input. Unbounded (vector) inputs impose no supply.
        float supply = 0f;
        bool hasConcrete = false;
        foreach (EffectiveScale e in inputs)
        {
            if (e.IsUnbounded) continue;
            hasConcrete = true;
            if (e.Value > supply) supply = e.Value;
        }

        // No concrete supply => purely vector content; rasterize at the output density.
        if (!hasConcrete) supply = outputScale;

        float w = policy.Kind switch
        {
            // Perf opt-out: clamp down to the output, but never below an ancestor PreserveSource floor.
            ResolutionPolicyKind.ClampToOutput => MathF.Max(MathF.Min(supply, outputScale), preserveFloor),
            // Quality opt-in: at least Factor×output, even from a lower-density input (SSAA on demand).
            ResolutionPolicyKind.Oversample => MathF.Max(supply, policy.Factor * outputScale),
            // Quality: keep the source density (the floor is carried to descendants by the caller).
            ResolutionPolicyKind.PreserveSource => MathF.Max(supply, preserveFloor),
            // Inherit (default): run at the supply density. The output scale is not a ceiling.
            _ => MathF.Max(supply, preserveFloor),
        };

        return MathF.Min(w, maxWorkingScale);
    }
}
