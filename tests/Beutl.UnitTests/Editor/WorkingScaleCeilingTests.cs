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

    // Export imposes NO working-scale quality ceiling: the delivery render follows the true supply density so an
    // authored high-density source (e.g. a 4096-px logo shrunk into a small box, supply ≈ 16) exports at full
    // fidelity. Allocatability is guaranteed per-buffer by ClampWorkingScaleToBufferBudget, not by this policy.
    [TestCase(1.0f)]
    [TestCase(0.5f)]
    [TestCase(8.0f)]
    public void Export_HasNoWorkingScaleCeiling(float outputScale)
    {
        Assert.That(WorkingScaleCeiling.Export(outputScale), Is.EqualTo(float.PositiveInfinity));
    }

    // The two ceilings differ (FR-037 preview/export divergence): preview is the tighter, finite bound that keeps
    // interactive renders cheap, while export is unbounded (delivery fidelity). This guards preview < export.
    [Test]
    public void Preview_IsTighterThanExport_AtFullScale()
    {
        Assert.That(WorkingScaleCeiling.Preview(1.0f), Is.LessThan(WorkingScaleCeiling.Export(1.0f)));
    }
}
