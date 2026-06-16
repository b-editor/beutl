namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Pinned tolerances for the feature-003 golden suite (plan.md / data-model.md).
internal static class GoldenThresholds
{
    /// <summary>Minimum SSIM for a reduced-scale render vs the full-scale reference (exact effects).</summary>
    public const double ExactSsimMin = 0.99;

    /// <summary>Maximum mean-absolute-error (linear) for an exact reduced-scale render.</summary>
    public const double ExactMaeMax = 0.02;

    // No mixed-scale composite-seam threshold: a seam test needs two adjacent regions at DIFFERENT working
    // densities, which no built-in effect produces (every boundary runs at its supply density). Deferred until
    // a custom FilterEffectRenderNode overrides Process to drive a divergent working scale.

    /// <summary>
    /// A tolerance, not a quality gate: a supersampled render's SSIM-to-truth may fall below the 1:1 render's
    /// by at most this much (ssimSS ≥ ssim11 − margin in <see cref="ExportSupersampleTests"/>). The SSAA quality
    /// gate is the strict MAE-to-truth decrease asserted alongside it.
    /// </summary>
    public const double SupersampleSsimMargin = 0.01;
}
