namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Pinned tolerances for the feature-003 golden suite (plan.md / data-model.md).
internal static class GoldenThresholds
{
    /// <summary>Minimum SSIM for a reduced-scale render vs the full-scale reference (exact effects).</summary>
    public const double ExactSsimMin = 0.985;

    /// <summary>Maximum mean-absolute-error (linear) for an exact reduced-scale render.</summary>
    public const double ExactMaeMax = 0.02;

    // NOTE: a mixed-scale composite-seam threshold lived here, but a meaningful seam test needs two adjacent
    // regions at DIFFERENT working densities, which no built-in effect can produce (every boundary runs at its
    // supply density). It is deferred until a custom FilterEffectRenderNode that overrides ResolveWorkingScale
    // exists to drive a divergent working scale.

    /// <summary>
    /// Degradation TOLERANCE, not a quality gate: a supersampled render's SSIM-to-truth may fall below the
    /// 1:1 render's by at most this much (ssimSS ≥ ssim11 − margin in <see cref="ExportSupersampleTests"/>).
    /// The actual SSAA quality gate is the strict MAE-to-truth decrease asserted alongside it.
    /// </summary>
    public const double SupersampleSsimMargin = 0.01;
}
