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
        Assert.That(RenderScaleUtilities.SanitizeMaxWorkingScale(value), Is.EqualTo(float.PositiveInfinity));
    }

    [TestCase(1f)]
    [TestCase(2.5f)]
    [TestCase(float.PositiveInfinity)]
    public void SanitizeMaxWorkingScale_FiniteOrInfinitePositive_PassesThrough(float value)
    {
        Assert.That(RenderScaleUtilities.SanitizeMaxWorkingScale(value), Is.EqualTo(value));
    }

    [TestCaseSource(nameof(DegenerateCeilings))]
    public void ResolveWorkingScale_DegenerateCeiling_DoesNotPropagate(float maxWorkingScale)
    {
        // Supply equals outputScale (2); a degenerate ceiling must not drag it to NaN/0 via MathF.Min.
        float w = RenderScaleUtilities.ResolveWorkingScale(
            ReadOnlySpan<EffectiveScale>.Empty, outputScale: 2f, maxWorkingScale: maxWorkingScale);

        Assert.That(w, Is.EqualTo(2f));
    }

    [Test]
    public void ResolveWorkingScale_FiniteCeiling_CapsSupply()
    {
        float w = RenderScaleUtilities.ResolveWorkingScale(
            ReadOnlySpan<EffectiveScale>.Empty, outputScale: 4f, maxWorkingScale: 3f);

        Assert.That(w, Is.EqualTo(3f));
    }

    [Test]
    public void ResolveWorkingScale_UnboundedInput_DoesNotRaiseSupply()
    {
        // Unbounded (vector) inputs are excluded from the supply max (FR-019).
        float w = RenderScaleUtilities.ResolveWorkingScale([EffectiveScale.Unbounded], outputScale: 2f);

        Assert.That(w, Is.EqualTo(2f));
    }

    [Test]
    public void ResolveWorkingScale_ConcreteSupplyAboveOutput_IgnoresUnboundedAndTracksSupply()
    {
        // The Unbounded sentinel is skipped; the concrete supply (10) drives the result.
        float w = RenderScaleUtilities.ResolveWorkingScale(
            [EffectiveScale.Unbounded, EffectiveScale.At(10f)], outputScale: 2f);

        Assert.That(w, Is.EqualTo(10f));
    }

    [Test]
    public void ResolveWorkingScale_ConcreteSupplyExceedsFiniteCeiling_CapsToCeiling()
    {
        // A finite ceiling caps a concrete supply that exceeds it.
        float w = RenderScaleUtilities.ResolveWorkingScale(
            [EffectiveScale.At(8f)], outputScale: 2f, maxWorkingScale: 5f);

        Assert.That(w, Is.EqualTo(5f));
    }

    [Test]
    public void ResolveWorkingScale_ConcreteSupplyUnderInfiniteCeiling_TracksSupply()
    {
        float w = RenderScaleUtilities.ResolveWorkingScale(
            [EffectiveScale.At(8f)], outputScale: 2f, maxWorkingScale: float.PositiveInfinity);

        Assert.That(w, Is.EqualTo(8f));
    }

    // These guard tests pin the Sanitize call at each public entry point so dropping it fails loudly.
    [TestCaseSource(nameof(DegenerateCeilings))]
    public void RenderNodeRenderer_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        using var node = new ContainerRenderNode();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                OutputScale = 1,
                MaxWorkingScale = maxWorkingScale,
                UseRenderCache = false,
            });

        Assert.That(renderer.Options.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    [Test]
    public void RenderNodeRenderer_FinitePositiveCeiling_PassesThrough()
    {
        using var node = new ContainerRenderNode();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                OutputScale = 1,
                MaxWorkingScale = 3,
                UseRenderCache = false,
            });

        Assert.That(renderer.Options.MaxWorkingScale, Is.EqualTo(3));
    }

    [TestCaseSource(nameof(DegenerateCeilings))]
    public void ImmediateCanvas_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        using var renderTarget = RenderTarget.CreateNull(1, 1);
        using var canvas = new ImmediateCanvas(renderTarget, density: 1f, maxWorkingScale: maxWorkingScale);

        Assert.That(canvas.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    [Test]
    public void ImmediateCanvas_FinitePositiveCeiling_PassesThrough()
    {
        using var renderTarget = RenderTarget.CreateNull(1, 1);
        using var canvas = new ImmediateCanvas(renderTarget, density: 1f, maxWorkingScale: 3f);

        Assert.That(canvas.MaxWorkingScale, Is.EqualTo(3f));
    }

    [TestCaseSource(nameof(DegenerateCeilings))]
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

    [TestCaseSource(nameof(DegenerateCeilings))]
    public void CustomFilterEffectContext_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: maxWorkingScale);

        Assert.Multiple(() =>
        {
            Assert.That(context.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
            Assert.That(context.Intent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(context.Purpose, Is.EqualTo(RenderRequestPurpose.Auxiliary));
        });
    }

    // SanitizeCeiling also logs a warning on substitution, but only the stored value is pinned here;
    // the warning emission is not observed.
    [TestCaseSource(nameof(DegenerateCeilings))]
    public void FilterEffectActivator_DegenerateCeiling_StoredAsPositiveInfinity(float maxWorkingScale)
    {
        using var targets = new EffectTargets();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: maxWorkingScale);

        Assert.That(activator.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    [Test]
    public void FilterEffectActivator_FinitePositiveCeiling_PassesThrough()
    {
        using var targets = new EffectTargets();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: 3f);

        Assert.That(activator.MaxWorkingScale, Is.EqualTo(3f));
    }
}
