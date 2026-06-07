namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Pinned tolerances for the feature-003 golden suite (plan.md / data-model.md).
internal static class GoldenThresholds
{
    /// <summary>Minimum SSIM for a reduced-scale render vs the full-scale reference (exact effects).</summary>
    public const double ExactSsimMin = 0.985;

    /// <summary>Maximum mean-absolute-error (linear) for an exact reduced-scale render.</summary>
    public const double ExactMaeMax = 0.02;

    // NOTE: a mixed-scale composite-seam threshold lived here, but a meaningful seam test needs two adjacent
    // regions at DIFFERENT working densities, which no built-in effect can produce (none uses ClampToOutput /
    // Oversample, and PreserveSource was removed). It is deferred until a custom-policy test effect exists.

    /// <summary>SSIM margin a supersampled render must beat the non-supersampled one by (lower aliasing).</summary>
    public const double SupersampleSsimMargin = 0.01;
}
