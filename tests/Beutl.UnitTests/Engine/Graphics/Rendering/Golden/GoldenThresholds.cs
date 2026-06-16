namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Pinned tolerances for the golden suite.
internal static class GoldenThresholds
{
    /// <summary>Minimum SSIM for a reduced-scale render vs the full-scale reference (exact effects).</summary>
    public const double ExactSsimMin = 0.99;

    /// <summary>Maximum mean-absolute-error (linear) for an exact reduced-scale render.</summary>
    public const double ExactMaeMax = 0.02;

    /// <summary>
    /// Tolerance for supersampled SSIM-to-truth vs 1:1 (ssimSS >= ssim11 - margin).
    /// </summary>
    public const double SupersampleSsimMargin = 0.01;
}
