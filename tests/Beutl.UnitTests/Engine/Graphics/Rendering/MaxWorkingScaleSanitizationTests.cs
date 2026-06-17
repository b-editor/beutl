using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// A degenerate working-scale ceiling (NaN / non-positive) must be treated as "no ceiling" (+Inf)
// at every public entry point, so it never propagates into the resolved working scale.
[TestFixture]
public class MaxWorkingScaleSanitizationTests
{
    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NegativeInfinity)]
    public void SanitizeMaxWorkingScale_DegenerateValue_BecomesPositiveInfinity(float value)
    {
        Assert.That(RenderNodeContext.SanitizeMaxWorkingScale(value), Is.EqualTo(float.PositiveInfinity));
    }

    [TestCase(1f)]
    [TestCase(2.5f)]
    [TestCase(float.PositiveInfinity)]
    public void SanitizeMaxWorkingScale_FiniteOrInfinitePositive_PassesThrough(float value)
    {
        Assert.That(RenderNodeContext.SanitizeMaxWorkingScale(value), Is.EqualTo(value));
    }

    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void ResolveWorkingScale_DegenerateCeiling_DoesNotPropagate(float maxWorkingScale)
    {
        // With no concrete inputs the supply equals outputScale (2), and a degenerate
        // ceiling must not pull the result to NaN/0 via MathF.Min.
        float w = RenderNodeContext.ResolveWorkingScale(
            ReadOnlySpan<EffectiveScale>.Empty, outputScale: 2f, maxWorkingScale: maxWorkingScale);

        Assert.That(w, Is.EqualTo(2f));
    }

    [Test]
    public void ResolveWorkingScale_FiniteCeiling_CapsSupply()
    {
        float w = RenderNodeContext.ResolveWorkingScale(
            ReadOnlySpan<EffectiveScale>.Empty, outputScale: 4f, maxWorkingScale: 3f);

        Assert.That(w, Is.EqualTo(3f));
    }

    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void RenderNodeContext_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        var context = new RenderNodeContext([], outputScale: 1f, maxWorkingScale: maxWorkingScale);

        Assert.That(context.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }
}
