using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class GpuPassFusionFeature003RegressionTests
{
    private static readonly PixelSize s_frame = new(192, 128);

    public static IEnumerable<TestCaseData> DensityCases()
    {
        yield return DensityCase(
            "all-vector uses the output floor",
            [EffectiveScale.Unbounded, EffectiveScale.Unbounded],
            outputScale: 0.5f,
            maxWorkingScale: float.PositiveInfinity,
            expected: 0.5f);
        yield return DensityCase(
            "sub-output proxy is floored for delivery",
            [EffectiveScale.At(0.5f)],
            outputScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            expected: 1);
        yield return DensityCase(
            "matching proxy stays cheap in preview",
            [EffectiveScale.At(0.5f)],
            outputScale: 0.5f,
            maxWorkingScale: 1,
            expected: 0.5f);
        yield return DensityCase(
            "dense bitmap supply is not capped by output",
            [EffectiveScale.At(4), EffectiveScale.Unbounded],
            outputScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            expected: 4);
        yield return DensityCase(
            "densest concrete input wins",
            [EffectiveScale.At(0.5f), EffectiveScale.At(2), EffectiveScale.Unbounded],
            outputScale: 1,
            maxWorkingScale: float.PositiveInfinity,
            expected: 2);
        yield return DensityCase(
            "preview maximum working scale caps dense supply",
            [EffectiveScale.At(8), EffectiveScale.At(1)],
            outputScale: 1,
            maxWorkingScale: 2,
            expected: 2);
        yield return DensityCase(
            "supersample output is a floor",
            [EffectiveScale.At(0.5f), EffectiveScale.Unbounded],
            outputScale: 2,
            maxWorkingScale: float.PositiveInfinity,
            expected: 2);
    }

    [TestCaseSource(nameof(DensityCases))]
    public void Feature003SupplyDrivenDensityMatrix_RemainsStable(
        EffectiveScale[] inputs,
        float outputScale,
        float maxWorkingScale,
        float expected)
    {
        float resolved = RenderScaleUtilities.ResolveWorkingScale(
            inputs,
            outputScale,
            maxWorkingScale);

        Assert.That(resolved, Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    public void Feature003TransformDensityAndDimensionRounding_RemainStable()
    {
        EffectiveScale anisotropic = TransformRenderNode.RescaleDensity(
            EffectiveScale.At(2),
            Matrix.CreateScale(0.5f, 0.25f));
        EffectiveScale rotation = TransformRenderNode.RescaleDensity(
            EffectiveScale.At(2),
            Matrix.CreateRotation(MathF.PI / 3));
        (int width, int height) scaleOne = CustomFilterEffectContext.DeviceBufferSize(
            new Rect(0, 0, 100.7f, 50.2f),
            1);
        (int width, int height) dense = CustomFilterEffectContext.DeviceBufferSize(
            new Rect(0, 0, 100.3f, 50.1f),
            2);

        Assert.Multiple(() =>
        {
            Assert.That(anisotropic, Is.EqualTo(EffectiveScale.At(8)),
                "the most detailed transformed axis defines available density");
            Assert.That(rotation, Is.EqualTo(EffectiveScale.At(2)),
                "a pure rotation must not invent or discard density");
            Assert.That(scaleOne, Is.EqualTo((101, 51)),
                "the scale-one allocation retains the canonical rounded footprint");
            Assert.That(dense, Is.EqualTo((201, 101)),
                "non-unit device allocation uses ceil after scaling");
        });
    }

    [Test]
    public void Feature003BufferClampAndCacheIdentity_IncludeResolvedDensity()
    {
        var bounds = new Rect(0, 0, 10_000.25f, 20);
        float clamped = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(bounds, 4);
        RenderOutputCacheIdentity atOne = CreateCacheIdentity(bounds, density: 1);
        RenderOutputCacheIdentity atTwo = CreateCacheIdentity(bounds, density: 2);
        RenderOutputCacheIdentity atTwoAgain = CreateCacheIdentity(bounds, density: 2);

        Assert.Multiple(() =>
        {
            Assert.That(clamped, Is.LessThan(4));
            Assert.That(Math.Ceiling(bounds.Width * clamped),
                Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(atOne, Is.Not.EqualTo(atTwo),
                "a density change must invalidate a materialized output cache entry");
            Assert.That(atTwo, Is.EqualTo(atTwoAgain),
                "equal resolved density and runtime components must be cache-stable");
        });
    }

    [TestCase(RepresentativeContent.Vector)]
    [TestCase(RepresentativeContent.Bitmap)]
    [TestCase(RepresentativeContent.Text)]
    [Category("GpuPassFusionGpu")]
    public void Feature003ScaleOneGoldenAnchor_IsByteStableAndNonVacuous(
        RepresentativeContent content)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap first = RenderRepresentative(content, 1);
            using Bitmap second = RenderRepresentative(content, 1);

            GoldenImageHarness.AssertByteIdentical(first, second);
            Assert.That(SumAbsoluteChannels(first), Is.GreaterThan(1),
                "a byte-stable transparent result is not a useful golden anchor");
        });
    }

    [TestCase(RepresentativeContent.Vector)]
    [TestCase(RepresentativeContent.Bitmap)]
    [TestCase(RepresentativeContent.Text)]
    [Category("GpuPassFusionGpu")]
    public void Feature003HalfPreviewAndDoubleSupersample_PreserveLogicalContentAndDeviceSizes(
        RepresentativeContent content)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap full = RenderRepresentative(content, 1);
            using Bitmap half = RenderRepresentative(content, 0.5f);
            using Bitmap halfDelivered = GoldenImageHarness.MitchellResampleTo(half, s_frame);
            using Bitmap supersampled = RenderRepresentative(content, 2);
            using Bitmap doubleDelivered = GoldenImageHarness.MitchellResampleTo(supersampled, s_frame);

            double halfSsim = ImageMetrics.Ssim(full, halfDelivered);
            double halfMae = ImageMetrics.MeanAbsoluteError(full, halfDelivered);
            double doubleSsim = ImageMetrics.Ssim(full, doubleDelivered);
            double doubleMae = ImageMetrics.MeanAbsoluteError(full, doubleDelivered);
            TestContext.WriteLine(
                $"{content}: half SSIM={halfSsim:F4} MAE={halfMae:F4}; "
                + $"double SSIM={doubleSsim:F4} MAE={doubleMae:F4}");
            GoldenLimits limits = GoldenLimits.For(content);

            Assert.Multiple(() =>
            {
                Assert.That(half.Width, Is.EqualTo(96));
                Assert.That(half.Height, Is.EqualTo(64));
                Assert.That(supersampled.Width, Is.EqualTo(384));
                Assert.That(supersampled.Height, Is.EqualTo(256));
                Assert.That(halfSsim, Is.GreaterThanOrEqualTo(limits.HalfSsim),
                    "reduced preview lost the representative logical structure");
                Assert.That(halfMae, Is.LessThanOrEqualTo(limits.HalfMae));
                Assert.That(doubleSsim, Is.GreaterThanOrEqualTo(limits.DoubleSsim),
                    "supersampled output diverged from the scale-one logical anchor");
                Assert.That(doubleMae, Is.LessThanOrEqualTo(limits.DoubleMae));
            });
        });
    }

    [Test]
    public void CharacterizedPreviewAllocationFailure_DropsTheEffectOutputWithoutThrowing()
    {
        using EffectTargets targets = CreateNonAllocatableTargets();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 2);

        Assert.That(() => activator.Flush(), Throws.Nothing);
        Assert.That(activator.CurrentTargets, Is.Empty,
            "preview keeps the current-main drop-on-allocation-failure outcome");
    }

    [Test]
    public void CharacterizedDeliveryAllocationFailure_FailsFastInsteadOfDroppingContent()
    {
        using EffectTargets targets = CreateNonAllocatableTargets();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: float.PositiveInfinity);

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => activator.Flush());
        Assert.That(exception!.Message, Does.StartWith("Effect flush buffer allocation failed"));
    }

    private static TestCaseData DensityCase(
        string name,
        EffectiveScale[] inputs,
        float outputScale,
        float maxWorkingScale,
        float expected)
        => new TestCaseData(inputs, outputScale, maxWorkingScale, expected).SetName(name);

    private static RenderOutputCacheIdentity CreateCacheIdentity(Rect bounds, float density)
    {
        var fragment = new RenderFragmentReference(
            RenderFragmentKind.MaterializedInput,
            bounds,
            EffectiveScale.At(density),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            inputs: null,
            payload: null,
            hitTest: null);
        var requestId = new RenderRequestId(1);
        return new RenderOutputCacheIdentity(
            candidateKey: "feature-003-density",
            RenderFragmentOutputIdentity.Create(fragment, requestId),
            bounds,
            RequiredRegion.Region(bounds),
            density,
            RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            new RenderCacheDeviceContextIdentity("device", "context"));
    }

    private static Bitmap RenderRepresentative(RepresentativeContent content, float scale)
    {
        if (content == RepresentativeContent.Bitmap)
            return RenderMaterializedBitmap(scale);

        using Drawable.Resource resource = content switch
        {
            RepresentativeContent.Vector => CreateVector(),
            RepresentativeContent.Text => CreateText(),
            _ => throw new ArgumentOutOfRangeException(nameof(content), content, null),
        };
        return GoldenImageHarness.RenderAtScale(resource, s_frame, scale);
    }

    private static Drawable.Resource CreateVector()
    {
        var shape = new EllipseShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = 126;
        shape.Height.CurrentValue = 78;
        shape.Fill.CurrentValue = Brushes.OrangeRed;
        return shape.ToResource(CompositionContext.Default);
    }

    private static Bitmap RenderMaterializedBitmap(float scale)
    {
        var sourceSize = new PixelSize(96, 64);
        using RenderTarget source = RenderTarget.Create(sourceSize.Width, sourceSize.Height)
                                    ?? throw new InvalidOperationException("Could not allocate bitmap source.");
        using (var sourceCanvas = new ImmediateCanvas(source, 1, logicalSize: sourceSize.ToSize(1)))
        {
            sourceCanvas.Clear(Colors.CornflowerBlue);
            sourceCanvas.DrawRectangle(new Rect(12, 10, 72, 44), Brushes.Resource.OrangeRed, null);
        }

        int width = (int)MathF.Ceiling(s_frame.Width * scale);
        int height = (int)MathF.Ceiling(s_frame.Height * scale);
        using RenderTarget destination = RenderTarget.Create(width, height)
                                         ?? throw new InvalidOperationException("Could not allocate bitmap destination.");
        using (var destinationCanvas = new ImmediateCanvas(
                   destination,
                   scale,
                   logicalSize: s_frame.ToSize(1)))
        {
            destinationCanvas.Clear(Colors.Black);
            using var node = new MaterializedBitmapNode(source);
            using var renderer = new RenderNodeRenderer(
                node,
                new RenderNodeRendererOptions
                {
                    Intent = RenderIntent.Delivery,
                    TargetDomain = new Rect(default, s_frame.ToSize(1)),
                    OutputScale = scale,
                    MaxWorkingScale = float.PositiveInfinity,
                    UseRenderCache = false,
                });
            renderer.Render(destinationCanvas);
        }

        return destination.Snapshot();
    }

    private static Drawable.Resource CreateText()
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var text = new TextBlock();
        text.AlignmentX.CurrentValue = AlignmentX.Center;
        text.AlignmentY.CurrentValue = AlignmentY.Center;
        text.FontFamily.CurrentValue = typeface.FontFamily;
        text.FontStyle.CurrentValue = typeface.Style;
        text.FontWeight.CurrentValue = typeface.Weight;
        text.Size.CurrentValue = 34;
        text.Fill.CurrentValue = Brushes.White;
        text.Text.CurrentValue = "Density";
        return text.ToResource(CompositionContext.Default);
    }

    private static EffectTargets CreateNonAllocatableTargets()
    {
        using RenderTarget source = RenderTarget.CreateNull(1, 1);
        return new EffectTargets
        {
            new EffectTarget(
                source,
                new Rect(10, 8, -1, 92),
                EffectiveScale.At(1)),
        };
    }

    private static double SumAbsoluteChannels(Bitmap bitmap)
    {
        double result = 0;
        foreach (ushort bits in bitmap.GetPixelSpan<ushort>())
            result += Math.Abs((float)BitConverter.UInt16BitsToHalf(bits));
        return result;
    }

    private sealed class MaterializedBitmapNode(RenderTarget source) : RenderNode
    {
        private static readonly Rect s_bounds = new(48, 32, 96, 64);

        public override void Process(RenderNodeContext context)
        {
            RenderResource<RenderTarget> target = context.Borrow(
                source,
                "feature-003-materialized-bitmap",
                version: 1);
            context.Publish(context.MaterializedInput(MaterializedInputDescription.FromRenderTarget(
                target,
                s_bounds,
                EffectiveScale.At(1),
                RenderHitTestContract.OutputBounds)));
        }
    }

    private readonly record struct GoldenLimits(
        double HalfSsim,
        double HalfMae,
        double DoubleSsim,
        double DoubleMae)
    {
        public static GoldenLimits For(RepresentativeContent content)
            => content == RepresentativeContent.Text
                // Full-hinted glyph rasterization is intentionally resolution-sensitive under feature 003.
                ? new GoldenLimits(0.75, 0.04, 0.92, 0.025)
                : new GoldenLimits(0.95, 0.035, 0.98, 0.02);
    }

    public enum RepresentativeContent
    {
        Vector,
        Bitmap,
        Text,
    }
}
