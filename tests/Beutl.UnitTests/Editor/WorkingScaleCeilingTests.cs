using Beutl.Models;

namespace Beutl.UnitTests.Editor;

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

    // Export has no working-scale ceiling; allocatability is per-buffer via BufferDimensionBudget.ClampWorkingScale.
    [Test]
    public void Export_HasNoWorkingScaleCeiling()
    {
        Assert.That(WorkingScaleCeiling.Export(), Is.EqualTo(float.PositiveInfinity));
    }
}
