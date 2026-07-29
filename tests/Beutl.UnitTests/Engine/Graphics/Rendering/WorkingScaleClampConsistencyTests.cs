using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// When the buffer clamp fires, context.WorkingScale must equal the allocated target.Scale.
[NonParallelizable]
[TestFixture]
public class WorkingScaleClampConsistencyTests
{
    // 4000 logical px × w 8 = 32000 px > MaxBufferDimension (16384) → the clamp must fire.
    private static readonly Rect s_pathologicalBounds = new(0, 0, 4000, 10);

    [Test]
    public void ExactClamp_NegativeOriginPreservesDensityWhenDeviceFootprintFits()
    {
        var bounds = new Rect(
            -0.5f,
            0,
            RenderScaleUtilities.MaxBufferDimension - 0.5f,
            1);
        PixelSize deviceSize = PixelRect.FromRect(bounds, 1).Size;
        float coarse = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(bounds, 1);
        float exact = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(bounds, 1);
        EffectiveScale planned = FilterEffectWorkingScalePolicy.ResolveMaterialized(
            [EffectiveScale.At(1)],
            [bounds],
            outputScale: 1,
            maxWorkingScale: 1);
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            workingScale: 1);

        Assert.Multiple(() =>
        {
            Assert.That(deviceSize.Width, Is.EqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(coarse, Is.EqualTo(1));
            Assert.That(exact, Is.EqualTo(1));
            Assert.That(planned, Is.EqualTo(EffectiveScale.At(1)));
            Assert.That(context.ResolveTargetDensity(bounds), Is.EqualTo(1));
        });
    }

    [Test]
    public void ExactClamp_TightensBelowTheCoarseEstimateWhenANegativeOriginAddsADevicePixel()
    {
        var bounds = new Rect(
            -0.5f,
            0,
            RenderScaleUtilities.MaxBufferDimension,
            1);
        PixelSize deviceSize = PixelRect.FromRect(bounds, 1).Size;
        float coarse = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(bounds, 1);
        float exact = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(bounds, 1);
        EffectiveScale planned = FilterEffectWorkingScalePolicy.ResolveMaterialized(
            [EffectiveScale.At(1)],
            [bounds],
            outputScale: 1,
            maxWorkingScale: 1);

        Assert.Multiple(() =>
        {
            Assert.That(deviceSize.Width, Is.EqualTo(RenderScaleUtilities.MaxBufferDimension + 1));
            Assert.That(coarse, Is.LessThan(1), "the estimate must account for the straddled pixel");
            Assert.That(exact, Is.LessThan(1));
            Assert.That(exact, Is.LessThanOrEqualTo(coarse));
            Assert.That(
                PixelRect.FromRect(bounds, coarse).Width,
                Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(
                PixelRect.FromRect(bounds, exact).Width,
                Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(planned.Value, Is.LessThan(1));
        });
    }

    [Test]
    public void MaterializationPolicy_PreservesExactFitAtNegativeOrigin()
    {
        var bounds = new Rect(
            -0.5f,
            0,
            RenderScaleUtilities.MaxBufferDimension - 0.5f,
            1);
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: bounds,
            owner: owner));
        var transaction = new NodeRecordingTransaction(
            new RenderRequestRecorder(request),
            new object(),
            []);
        var context = new RenderNodeContext(transaction);
        RenderFragmentHandle handle = context.OpaqueSource(OpaqueRenderDescription.Create(
            static _ => { },
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.None,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: "negative-origin-materialization-policy"));
        RenderFragmentReference reference = transaction.GetReference(handle);

        Assert.Multiple(() =>
        {
            Assert.That(reference.EffectiveScale, Is.EqualTo(EffectiveScale.At(1)));
            Assert.That(RenderMaterializationDensityPolicy.Clamp(reference, 1), Is.EqualTo(1));
            Assert.That(
                PixelRect.FromRect(reference.Bounds, 1).Width,
                Is.EqualTo(RenderScaleUtilities.MaxBufferDimension));
        });
    }

    [Test]
    public void RasterApronClamp_PreservesDensityWhenExactApronedFootprintFits()
    {
        var bounds = new Rect(
            -0.5f,
            0,
            RenderScaleUtilities.MaxBufferDimension - 2.5f,
            1);
        PixelRect footprint = RenderScaleUtilities.AddRasterApron(PixelRect.FromRect(bounds, 1));

        Assert.Multiple(() =>
        {
            Assert.That(footprint.Width, Is.EqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(
                RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(bounds, 1),
                Is.EqualTo(1));
        });
    }

    [Test]
    public void Flush_ClampWriteback_KeepsWorkingScaleEqualToBufferDensity()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create(4000, 10)!;
            using var targets = new EffectTargets
            {
                new EffectTarget(source, s_pathologicalBounds, EffectiveScale.At(1)),
            };
            using var builder = new SKImageFilterBuilder();
            using var activator = new FilterEffectActivator(
                targets,
                builder,
                RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary,
                outputScale: 1f,
                workingScale: 8f,
                maxWorkingScale: 8f);

            activator.Flush();

            float expected = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(s_pathologicalBounds, 8f);
            Assert.That(expected, Is.LessThan(8f), "the fixture must actually trigger the clamp");
            Assert.That(activator.WorkingScale, Is.EqualTo(expected));
            Assert.That(activator.CurrentTargets, Has.Count.EqualTo(1));
            Assert.That(activator.CurrentTargets[0].Scale.Value, Is.EqualTo(activator.WorkingScale),
                "the flushed buffer's density and the activator's WorkingScale must agree");
            Assert.That(activator.CurrentTargets[0].RenderTarget!.Width,
                Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
        });
    }

    [Test]
    public void CreateTarget_ClampsInsteadOfFailing_AndTagsTrueDensity()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var targets = new EffectTargets();
            var context = new CustomFilterEffectContext(
                targets,
                RenderIntent.Delivery,
                RenderRequestPurpose.Auxiliary,
                outputScale: 1f,
                workingScale: 8f);

            using EffectTarget target = context.CreateTarget(s_pathologicalBounds);

            Assert.That(target.IsEmpty, Is.False,
                "an oversized request must degrade density, not return an empty target");
            float expected = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(s_pathologicalBounds, 8f);
            Assert.That(target.Scale.Value, Is.EqualTo(expected));
            Assert.That(target.RenderTarget!.Width, Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
        });
    }

    [Test]
    public void Flush_PreviewAllocationFailure_DropsTargetWithoutThrowing()
    {
        using var targets = CreateInvalidFlushTargets();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: 8f);

        Assert.That(() => activator.Flush(), Throws.Nothing);
        Assert.Multiple(() =>
        {
            Assert.That(activator.Intent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(activator.Purpose, Is.EqualTo(RenderRequestPurpose.Auxiliary));
            Assert.That(activator.CurrentTargets, Is.Empty);
        });
    }

    [Test]
    public void Flush_DeliveryAllocationFailure_ThrowsInsteadOfDroppingTarget()
    {
        using var targets = CreateInvalidFlushTargets();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1f,
            workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity);

        var ex = Assert.Throws<InvalidOperationException>(() => activator.Flush());
        Assert.Multiple(() =>
        {
            Assert.That(activator.Intent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(activator.Purpose, Is.EqualTo(RenderRequestPurpose.Auxiliary));
            Assert.That(ex!.Message, Does.Contain("Effect flush buffer allocation failed"));
        });
    }

    private static EffectTargets CreateInvalidFlushTargets()
    {
        using RenderTarget source = RenderTarget.CreateNull(1, 1);
        return new EffectTargets
        {
            new EffectTarget(source, new Rect(0, 0, -1, 10), EffectiveScale.At(1)),
        };
    }
}
