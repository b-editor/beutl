using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// A mapped-input readback failure must decide degrade-vs-fail from the explicit
/// <see cref="RenderIntent"/>, not from the working-scale ceiling that happens to accompany it, and a
/// replacement allocation failure must keep Preview sources and fail Delivery renders.
/// </summary>
[TestFixture]
public sealed class MappedInputReadbackIntentTests
{
    [TestCase(float.PositiveInfinity)]
    [TestCase(2f)]
    public void DeliveryReadbackFailureFailsRegardlessOfTheWorkingScaleCeiling(float maxWorkingScale)
    {
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: maxWorkingScale);

        Assert.That(
            () => CustomFilterEffectContext.ThrowIfDeliveryReadbackFailure(
                context.Intent,
                new PixelRect(0, 0, 4, 3)),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains("4x3 px")
                .And.Message.Contains("delivery render fails"));
    }

    [TestCase(float.PositiveInfinity)]
    [TestCase(2f)]
    public void PreviewReadbackFailureDegradesRegardlessOfTheWorkingScaleCeiling(float maxWorkingScale)
    {
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: maxWorkingScale);

        Assert.That(
            () => CustomFilterEffectContext.ThrowIfDeliveryReadbackFailure(
                context.Intent,
                new PixelRect(0, 0, 4, 3)),
            Throws.Nothing);
    }

    [Test]
    public void PreviewReplacementAllocationFailureKeepsTheSource()
    {
        using EffectTarget source = CreateUnallocatableSourceTarget();
        using var targets = new EffectTargets { source.Clone() };
        var context = CreateContext(targets, RenderIntent.Preview);
        EffectTarget original = targets[0];
        using EffectTarget replacement = context.CreateTargetLike(original);

        Assert.That(replacement.IsEmpty, Is.True);
        Assert.That(targets[0], Is.SameAs(original));
        Assert.That(original.RenderTarget, Is.Not.Null);
    }

    [Test]
    public void DeliveryReplacementAllocationFailureIncludesTheDeviceFootprint()
    {
        using EffectTarget source = CreateUnallocatableSourceTarget();
        using var targets = new EffectTargets { source.Clone() };
        var context = CreateContext(targets, RenderIntent.Delivery);
        EffectTarget original = targets[0];

        Assert.That(
            () => context.CreateTargetLike(original),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains($"{int.MaxValue}x3 px")
                .And.Message.Contains("delivery render fails"));
        Assert.That(targets[0], Is.SameAs(original));
        Assert.That(original.RenderTarget, Is.Not.Null);
    }

    [Test]
    public void DeliveryUnmaterializedSourceIsALegitimateSkip()
    {
        using var targets = new EffectTargets { new EffectTarget() };
        var context = CreateContext(targets, RenderIntent.Delivery);
        using EffectTarget replacement = context.CreateTargetLike(targets[0]);

        Assert.That(replacement.IsEmpty, Is.True);
    }

    [Test]
    public void LayerEffectPreviewAllocationFailureKeepsTheSource()
    {
        using var source = new CpuRenderTarget(new PixelSize(1, 1));
        using var targets = new EffectTargets { new EffectTarget(source, Rect.Empty) };
        EffectTarget original = targets[0];

        Assert.That(
            () => ApplyCustomDirect(new LayerEffect(), Rect.Empty, targets, RenderIntent.Preview),
            Throws.Nothing);
        Assert.That(targets[0], Is.SameAs(original));
        Assert.That(original.RenderTarget, Is.Not.Null);
    }

    private static void ApplyCustomDirect(
        FilterEffect effect,
        Rect bounds,
        EffectTargets targets,
        RenderIntent intent)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var recording = new FilterEffectContext(bounds, outputScale: 1f, workingScale: 1f);
        recording.ApplyTransactional(effect, resource);
        IFEItem_Custom item = recording.GetOrderedItems().OfType<IFEItem_Custom>().Single();
        item.Accepts(CreateContext(targets, intent));
    }

    private static CustomFilterEffectContext CreateContext(EffectTargets targets, RenderIntent intent)
        => new(
            targets,
            intent,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: 1f);

    private static EffectTarget CreateUnallocatableSourceTarget()
    {
        using var target = new CpuRenderTarget(new PixelSize(int.MaxValue, 3));
        return new EffectTarget(target, new Rect(0, 0, 4, 3));
    }

    private sealed class CpuRenderTarget(PixelSize size)
        : RenderTarget(
            SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1, 1))
                ?? throw new InvalidOperationException("Could not create a test surface."),
            size.Width,
            size.Height);
}
