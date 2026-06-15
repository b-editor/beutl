using Beutl.Models;

namespace Beutl.UnitTests.Editor;

// feature 003 (S3): GPU-free CPU tests for the centralized FR-037 working-scale ceiling. Pins the preview/export
// formulas in CI even when the Vulkan golden suite is skipped, replacing three hand-copied
// `MathF.Max(8f, 4f * s)` / `2f * s` expressions that could drift.
[TestFixture]
public class WorkingScaleCeilingTests
{
    [TestCase(1.0f, 2.0f)]   // Full preview
    [TestCase(0.5f, 1.0f)]   // Half preview
    [TestCase(0.25f, 0.5f)]  // Quarter preview
    [TestCase(2.0f, 4.0f)]   // export SSAA skips the preview ceiling, but the formula is still total
    public void Preview_Is2xOutputScale(float outputScale, float expected)
    {
        Assert.That(WorkingScaleCeiling.Preview(outputScale), Is.EqualTo(expected).Within(1e-6));
    }

    // Export imposes NO working-scale ceiling: the delivery render follows the true supply density, so an authored
    // high-density source (e.g. a 4096-px logo shrunk into a small box, supply ≈ 16) exports at full fidelity.
    // Allocatability is guaranteed per-buffer by ClampWorkingScaleToBufferBudget, not by this policy.
    [Test]
    public void Export_HasNoWorkingScaleCeiling()
    {
        Assert.That(WorkingScaleCeiling.Export(), Is.EqualTo(float.PositiveInfinity));
    }

    // FR-037 preview/export divergence: preview is a finite bound that keeps interactive renders cheap, export is
    // unbounded for delivery fidelity. Guards preview < export.
    [Test]
    public void Preview_IsTighterThanExport_AtFullScale()
    {
        Assert.That(WorkingScaleCeiling.Preview(1.0f), Is.LessThan(WorkingScaleCeiling.Export()));
    }
}
