using Beutl.Models;

namespace Beutl.UnitTests.Editor;

// feature 003 (S3): CPU unit tests for the centralized FR-037 working-scale ceiling policy. Runs without a GPU,
// so it pins the preview/export ceiling formulas in CI even when the Vulkan golden suite is skipped — replacing
// the three hand-copied `MathF.Max(8f, 4f * s)` / `2f * s` expressions that previously could drift.
[TestFixture]
public class WorkingScaleCeilingTests
{
    [TestCase(1.0f, 2.0f)]   // Full preview
    [TestCase(0.5f, 1.0f)]   // Half preview
    [TestCase(0.25f, 0.5f)]  // Quarter preview
    [TestCase(2.0f, 4.0f)]   // (export SSAA would not use the preview ceiling, but the formula is total)
    public void Preview_Is2xOutputScale(float outputScale, float expected)
    {
        Assert.That(WorkingScaleCeiling.Preview(outputScale), Is.EqualTo(expected).Within(1e-6));
    }

    [TestCase(1.0f, 8.0f)]   // s_out 1: the constant floor 8 binds (4×1 = 4 < 8), so a 4K-into-1080 source is un-clipped
    [TestCase(0.5f, 8.0f)]   // reduced output still floored at 8
    [TestCase(2.0f, 8.0f)]   // 4×2 = 8 == floor
    [TestCase(4.0f, 16.0f)]  // 4×4 = 16 > 8, the multiplicative term binds
    public void Export_IsMax8And4xOutputScale(float outputScale, float expected)
    {
        Assert.That(WorkingScaleCeiling.Export(outputScale), Is.EqualTo(expected).Within(1e-6));
    }

    // The whole reason the two ceilings differ (FR-037 preview/export divergence): preview is the tighter bound,
    // so a high-density source's resolution-sensitive effects can run at a different working scale in Full
    // preview than in export. This guards that the policy keeps preview < export at s_out = 1.
    [Test]
    public void Preview_IsTighterThanExport_AtFullScale()
    {
        Assert.That(WorkingScaleCeiling.Preview(1.0f), Is.LessThan(WorkingScaleCeiling.Export(1.0f)));
    }
}
