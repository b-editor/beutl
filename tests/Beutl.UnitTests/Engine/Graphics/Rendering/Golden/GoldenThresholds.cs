namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Pinned tolerances for the feature-003 golden suite (plan.md / data-model.md).
internal static class GoldenThresholds
{
    /// <summary>Minimum SSIM for a reduced-scale render vs the full-scale reference (exact effects).</summary>
    public const double ExactSsimMin = 0.985;

    /// <summary>Maximum mean-absolute-error (linear) for an exact reduced-scale render.</summary>
    public const double ExactMaeMax = 0.02;

    /// <summary>Maximum per-pixel delta across a mixed-scale composite boundary (no visible seam).</summary>
    public const double SeamMaxDelta = 0.05;

    /// <summary>SSIM margin a supersampled render must beat the non-supersampled one by (lower aliasing).</summary>
    public const double SupersampleSsimMargin = 0.01;

    /// <summary>Output scales exercised by the supersample / reduced-scale golden cases.</summary>
    public static readonly float[] SupersampleFactors = [1.5f, 2.0f, 4.0f];
}
