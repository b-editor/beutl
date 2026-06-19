using Beutl.Graphics;
using Beutl.Graphics.Effects;
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
    [TestCase(float.NegativeInfinity)]
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

    [Test]
    public void ResolveWorkingScale_UnboundedInput_DoesNotRaiseSupply()
    {
        // Vector (Unbounded) inputs are excluded from the supply max (FR-019); supply stays at the output floor.
        float w = RenderNodeContext.ResolveWorkingScale([EffectiveScale.Unbounded], outputScale: 2f);

        Assert.That(w, Is.EqualTo(2f));
    }

    [Test]
    public void ResolveWorkingScale_ConcreteSupplyAboveOutput_IgnoresUnboundedAndTracksSupply()
    {
        // The concrete bitmap supply (10) drives the result; the Unbounded sentinel is skipped, not read as a density.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.Unbounded, EffectiveScale.At(10f)], outputScale: 2f);

        Assert.That(w, Is.EqualTo(10f));
    }

    [Test]
    public void ResolveWorkingScale_ConcreteSupplyExceedsFiniteCeiling_CapsToCeiling()
    {
        // A finite ceiling must cap a concrete supply that exceeds it, not only the output-scale floor.
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

    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void RenderNodeContext_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        var context = new RenderNodeContext([], outputScale: 1f, maxWorkingScale: maxWorkingScale);

        Assert.That(context.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    // The remaining public entry points just forward into SanitizeMaxWorkingScale. These guard tests pin that
    // forwarding at the public surface so a future refactor that drops the Sanitize call fails loudly instead of
    // silently storing NaN/0/-1. Only the cheap-to-construct entries are covered; ImmediateCanvas/Renderer need a
    // real SKSurface and are left to the helper-level coverage.

    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void RenderNodeProcessor_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        var processor = new RenderNodeProcessor(
            new ContainerRenderNode(), useRenderCache: false, outputScale: 1f, maxWorkingScale: maxWorkingScale);

        Assert.That(processor.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    [Test]
    public void RenderNodeProcessor_FinitePositiveCeiling_PassesThrough()
    {
        var processor = new RenderNodeProcessor(
            new ContainerRenderNode(), useRenderCache: false, outputScale: 1f, maxWorkingScale: 3f);

        Assert.That(processor.MaxWorkingScale, Is.EqualTo(3f));
    }

    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void BrushConstructor_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        var ctor = new BrushConstructor(
            default, brush: null, BlendMode.SrcOver, scale: 1f, maxWorkingScale: maxWorkingScale);

        Assert.That(ctor.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    [Test]
    public void BrushConstructor_FinitePositiveCeiling_PassesThrough()
    {
        var ctor = new BrushConstructor(default, brush: null, BlendMode.SrcOver, scale: 1f, maxWorkingScale: 3f);

        Assert.That(ctor.MaxWorkingScale, Is.EqualTo(3f));
    }

    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void CustomFilterEffectContext_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets, outputScale: 1f, workingScale: 1f, maxWorkingScale: maxWorkingScale);

        Assert.That(context.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    // FilterEffectActivator is the only entry point with logic beyond pure forwarding: SanitizeCeiling logs a
    // warning ONLY when the value actually changes. Pin both the substituted result and the no-substitution
    // pass-through so the 'sanitized != value' guard can't be inverted undetected.
    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void FilterEffectActivator_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        using var targets = new EffectTargets();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets, builder, outputScale: 1f, workingScale: 1f, maxWorkingScale: maxWorkingScale);

        Assert.That(activator.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    [Test]
    public void FilterEffectActivator_FinitePositiveCeiling_PassesThrough()
    {
        using var targets = new EffectTargets();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets, builder, outputScale: 1f, workingScale: 1f, maxWorkingScale: 3f);

        Assert.That(activator.MaxWorkingScale, Is.EqualTo(3f));
    }
}
