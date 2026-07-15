using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// A degenerate ceiling (NaN / non-positive) becomes "no ceiling" (+Inf) at every public entry point.
[TestFixture]
public class MaxWorkingScaleSanitizationTests
{
    // Shared degenerate-ceiling set, used by both the helper and the entry-point guard tests.
    private static IEnumerable<TestCaseData> DegenerateCeilings()
    {
        yield return new TestCaseData(float.NaN).SetName("{m}(NaN)");
        yield return new TestCaseData(0f).SetName("{m}(+0)");
        yield return new TestCaseData(-0f).SetName("{m}(-0)");
        yield return new TestCaseData(-1f).SetName("{m}(-1)");
        yield return new TestCaseData(float.NegativeInfinity).SetName("{m}(-Inf)");
    }

    [TestCaseSource(nameof(DegenerateCeilings))]
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

    [TestCaseSource(nameof(DegenerateCeilings))]
    public void ResolveWorkingScale_DegenerateCeiling_DoesNotPropagate(float maxWorkingScale)
    {
        // Supply equals outputScale (2); a degenerate ceiling must not drag it to NaN/0 via MathF.Min.
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

    [Test]
    public void ResolveWorkingScale_UnboundedInput_DoesNotRaiseSupply()
    {
        // Unbounded (vector) inputs are excluded from the supply max (FR-019).
        float w = RenderNodeContext.ResolveWorkingScale([EffectiveScale.Unbounded], outputScale: 2f);

        Assert.That(w, Is.EqualTo(2f));
    }

    [Test]
    public void ResolveWorkingScale_ConcreteSupplyAboveOutput_IgnoresUnboundedAndTracksSupply()
    {
        // The Unbounded sentinel is skipped; the concrete supply (10) drives the result.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.Unbounded, EffectiveScale.At(10f)], outputScale: 2f);

        Assert.That(w, Is.EqualTo(10f));
    }

    [Test]
    public void ResolveWorkingScale_ConcreteSupplyExceedsFiniteCeiling_CapsToCeiling()
    {
        // A finite ceiling caps a concrete supply that exceeds it.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(8f)], outputScale: 2f, maxWorkingScale: 5f);

        Assert.That(w, Is.EqualTo(5f));
    }

    [Test]
    public void ResolveWorkingScale_ConcreteSupplyUnderInfiniteCeiling_TracksSupply()
    {
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(8f)], outputScale: 2f, maxWorkingScale: float.PositiveInfinity);

        Assert.That(w, Is.EqualTo(8f));
    }

    // These guard tests pin the Sanitize call at each public entry point so dropping it fails loudly.
    // Renderer is omitted: its constructor allocates a GPU RenderTarget on the render thread.

    [TestCaseSource(nameof(DegenerateCeilings))]
    public void RenderNodeContext_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        var context = new RenderNodeContext([], RenderIntent.Delivery, outputScale: 1f, maxWorkingScale: maxWorkingScale);

        Assert.That(context.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    [TestCaseSource(nameof(DegenerateCeilings))]
    public void RenderNodeProcessor_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        var processor = new RenderNodeProcessor(
            new ContainerRenderNode(), useRenderCache: false, RenderIntent.Delivery,
            outputScale: 1f, maxWorkingScale: maxWorkingScale);

        Assert.That(processor.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    [Test]
    public void RenderNodeProcessor_FinitePositiveCeiling_PassesThrough()
    {
        var processor = new RenderNodeProcessor(
            new ContainerRenderNode(), useRenderCache: false, RenderIntent.Delivery,
            outputScale: 1f, maxWorkingScale: 3f);

        Assert.That(processor.MaxWorkingScale, Is.EqualTo(3f));
    }

    [TestCaseSource(nameof(DegenerateCeilings))]
    public void ImmediateCanvas_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        using var renderTarget = RenderTarget.CreateNull(1, 1);
        using var canvas = new ImmediateCanvas(renderTarget, RenderIntent.Delivery, density: 1f, maxWorkingScale: maxWorkingScale);

        Assert.That(canvas.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    [Test]
    public void ImmediateCanvas_FinitePositiveCeiling_PassesThrough()
    {
        using var renderTarget = RenderTarget.CreateNull(1, 1);
        using var canvas = new ImmediateCanvas(renderTarget, RenderIntent.Delivery, density: 1f, maxWorkingScale: 3f);

        Assert.That(canvas.MaxWorkingScale, Is.EqualTo(3f));
    }

    [TestCaseSource(nameof(DegenerateCeilings))]
    public void BrushConstructor_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        var ctor = new BrushConstructor(
            default, brush: null, BlendMode.SrcOver, RenderIntent.Delivery,
            scale: 1f, maxWorkingScale: maxWorkingScale);

        Assert.That(ctor.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    [Test]
    public void BrushConstructor_FinitePositiveCeiling_PassesThrough()
    {
        var ctor = new BrushConstructor(
            default, brush: null, BlendMode.SrcOver, RenderIntent.Delivery,
            scale: 1f, maxWorkingScale: 3f);

        Assert.That(ctor.MaxWorkingScale, Is.EqualTo(3f));
    }
}
