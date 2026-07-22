using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
[NonParallelizable]
public sealed class LegacyFilterPhysicalFootprintTests
{
    private static readonly Rect s_oversizedSourceBounds = new(0, 0, 16_384, 1);
    private static readonly Rect s_runtimeBounds = new(0.5f, 0, 1, 1);
    private static EffectiveScale s_observedMaterializedScale;
    private static PixelRect s_observedMaterializedDeviceBounds;
    private static Rect s_observedMaterializedRasterBounds;
    private static EffectiveScale s_observedNormalizedScale;
    private static PixelRect s_observedNormalizedDeviceBounds;
    private static Rect s_observedNormalizedRasterBounds;

    [Test]
    public void CreateTargetLike_MovedBounds_PreservesAllocationMetadata()
    {
        var allocationBounds = new Rect(0.25f, 0.25f, 1, 1);
        var movedBounds = allocationBounds.Translate(new Vector(20, 30));
        var originalBounds = new Rect(0, 0, 1, 1);
        var deviceBounds = new PixelRect(0, 0, 2, 2);
        using CpuRenderTarget backing = CreatePatternRenderTarget(2, 2);
        using var targets = new EffectTargets
        {
            new EffectTarget(backing, allocationBounds, EffectiveScale.At(1), deviceBounds)
            {
                Bounds = movedBounds,
                OriginalBounds = originalBounds,
            },
        };
        EffectTarget source = targets.Single();
        Rect expectedRasterBounds = source.RasterBounds;
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        using EffectTarget replacement = context.CreateTargetLike(source);

        Assert.Multiple(() =>
        {
            Assert.That(replacement.IsEmpty, Is.False);
            Assert.That(replacement.Bounds, Is.EqualTo(source.Bounds));
            Assert.That(replacement.OriginalBounds, Is.EqualTo(source.OriginalBounds));
            Assert.That(replacement.DeviceBounds, Is.EqualTo(source.DeviceBounds));
            Assert.That(replacement.RasterBounds, Is.EqualTo(expectedRasterBounds));
            Assert.That(replacement.Scale, Is.EqualTo(source.Scale));
        });
    }

    [Test]
    public void FractionalOriginIdentityCustom_PreservesCanonicalBackingByteForByte()
    {
        var bounds = new Rect(0.25f, 0, 1, 1);
        using EffectTargets targets = CreatePatternTargets(bounds);
        RenderTarget originalBacking = targets[0].RenderTarget!;
        using Bitmap before = originalBacking.Snapshot();
        ushort[] expected = before.GetPixelSpan<ushort>().ToArray();
        var effect = new WorkingScaleProbeEffect(static context =>
            context.CustomEffect(
                0,
                static (_, _) => { },
                static (_, current) => current));

        ApplyDirect(effect, bounds, targets);

        EffectTarget actual = targets.Single();
        using Bitmap after = actual.RenderTarget!.Snapshot();
        PixelRect semanticDeviceBounds = PixelRect.FromRect(actual.Bounds, actual.Scale.Value);
        Assert.Multiple(() =>
        {
            Assert.That(actual.RenderTarget, Is.SameAs(originalBacking),
                "a compatible same-density target should take the forced-flush fast path");
            Assert.That(after.Width, Is.EqualTo(2));
            Assert.That(after.Height, Is.EqualTo(1));
            Assert.That(after.GetPixelSpan<ushort>().SequenceEqual(expected), Is.True,
                "identity custom execution must preserve both fractional-origin source pixels");
            Assert.That(actual.DeviceBounds, Is.EqualTo(new PixelRect(0, 0, 2, 1)));
            Assert.That(actual.RasterBounds, Is.EqualTo(actual.DeviceBounds.ToRect(actual.Scale.Value)));
            Assert.That(Contains(actual.DeviceBounds, semanticDeviceBounds), Is.True);
        });
    }

    [Test]
    public void FractionalPositiveOriginBlur_IsTranslationEquivalentToOriginBaseline()
    {
        var originBounds = new Rect(0.25f, 0.25f, 1, 1);
        var translation = new Vector(20, 30);

        TargetSnapshot origin = ApplyBlur(originBounds);
        TargetSnapshot translated = ApplyBlur(originBounds.Translate(translation));

        Assert.Multiple(() =>
        {
            Assert.That(translated.Bounds, Is.EqualTo(origin.Bounds.Translate(translation)));
            Assert.That(
                translated.DeviceBounds,
                Is.EqualTo(origin.DeviceBounds.Translate(new PixelPoint(20, 30))));
            Assert.That(translated.RasterBounds, Is.EqualTo(origin.RasterBounds.Translate(translation)));
            Assert.That(translated.Pixels.SequenceEqual(origin.Pixels), Is.True,
                "moving a fractional-origin input must not move or clip its pixels inside the blur backing");
            Assert.That(origin.Pixels.Any(static value => value != 0), Is.True);
        });
    }

    [Test]
    public void PositionDependentSkiaBounds_PublishesCurrentOffsetAndContainsSemanticBounds()
    {
        var inputBounds = new Rect(2.25f, 0.25f, 1, 1);
        using EffectTargets targets = CreatePatternTargets(inputBounds);
        var effect = new WorkingScaleProbeEffect(static context =>
            context.AppendSkiaFilter(
                0,
                static (_, input, _) => SKImageFilter.CreateBlur(1, 1, input),
                static (_, bounds) => bounds.Translate(new Vector(bounds.X, 0))));

        ApplyDirect(effect, inputBounds, targets);

        EffectTarget actual = targets.Single();
        Rect expectedBounds = inputBounds.Translate(new Vector(inputBounds.X, 0));
        PixelRect semanticDeviceBounds = PixelRect.FromRect(expectedBounds, actual.Scale.Value);
        Assert.Multiple(() =>
        {
            Assert.That(actual.Bounds, Is.EqualTo(expectedBounds));
            Assert.That(actual.OriginalBounds, Is.EqualTo(new Rect(0, 0, 1, 1)));
            Assert.That(actual.DeviceBounds, Is.EqualTo(new PixelRect(4, 0, 2, 2)),
                "the published origin must use the current semantic-to-local offset");
            Assert.That(actual.RasterBounds, Is.EqualTo(actual.DeviceBounds.ToRect(actual.Scale.Value)));
            Assert.That(Contains(actual.DeviceBounds, semanticDeviceBounds), Is.True);
        });
    }

    [Test]
    public void ZeroCrossingFractionalOrigins_BlurPreservesIntegerTranslationWithSharedBacking()
    {
        var originBounds = new Rect(-0.25f, -0.25f, 1, 1);
        var translation = new Vector(1, 1);
        using var sharedBacking = CreatePatternRenderTarget(2, 2);

        TargetSnapshot origin = ApplyBlur(
            originBounds,
            sharedBacking,
            new PixelRect(-1, -1, 2, 2));
        TargetSnapshot translated = ApplyBlur(
            originBounds.Translate(translation),
            sharedBacking,
            new PixelRect(0, 0, 2, 2));

        Assert.Multiple(() =>
        {
            Assert.That(translated.Bounds, Is.EqualTo(origin.Bounds.Translate(translation)));
            Assert.That(
                translated.DeviceBounds,
                Is.EqualTo(origin.DeviceBounds.Translate(new PixelPoint(1, 1))));
            Assert.That(translated.RasterBounds, Is.EqualTo(origin.RasterBounds.Translate(translation)));
            Assert.That(translated.Pixels.SequenceEqual(origin.Pixels), Is.True,
                "integer translation across zero must preserve the isolated backing pixels");
            Assert.That(origin.Pixels.Any(static value => value != 0), Is.True);
        });
    }

    [Test]
    public void MaterializeLegacyTarget_ClampsRuntimePhysicalUnionBeforeAllocation()
    {
        s_observedMaterializedScale = default;
        s_observedMaterializedDeviceBounds = default;
        s_observedMaterializedRasterBounds = default;
        var factory = new TrackingTargetFactory();
        var effect = new WorkingScaleProbeEffect(static context =>
        {
            // Consume the segment's initial working-scale policy so the following legacy segment
            // reaches MaterializeLegacyTarget without a forced final activator flush.
            context.Shader(ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return color; }"));
            context.CustomEffect(
                s_runtimeBounds,
                static (bounds, execution) =>
                {
                    foreach (EffectTarget target in execution.Targets)
                        target.Bounds = bounds;
                },
                static (bounds, _) => bounds);
        });
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(EffectiveScale.At(1), s_oversizedSourceBounds),
            new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default)),
            new MaterializedMetadataProbeNode());
        using var renderer = new RenderNodeRenderer(
            pipeline,
            new RenderNodeRendererOptions
            {
                UseRenderCache = false,
                OutputScale = 1,
                MaxWorkingScale = 1,
                TargetFactory = factory,
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(factory.Allocations, Is.Not.Empty);
            Assert.That(factory.Allocations.All(static size =>
                    size.Width <= RenderScaleUtilities.MaxBufferDimension
                    && size.Height <= RenderScaleUtilities.MaxBufferDimension),
                Is.True,
                "the former 16385px normalization must be clamped before the pool allocation");
            Assert.That(s_observedMaterializedScale.IsUnbounded, Is.False);
            Assert.That(s_observedMaterializedScale.Value, Is.LessThan(1));
            Assert.That(s_observedMaterializedDeviceBounds.Width,
                Is.EqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(s_observedMaterializedRasterBounds,
                Is.EqualTo(s_observedMaterializedDeviceBounds.ToRect(
                    s_observedMaterializedScale.Value)));
            Assert.That(
                Contains(
                    s_observedMaterializedDeviceBounds,
                    PixelRect.FromRect(s_runtimeBounds, s_observedMaterializedScale.Value)),
                Is.True);
        });
    }

    [Test]
    public void TypedSuffixNormalizeInput_ClampsRuntimePhysicalUnionAndPublishesActualDensity()
    {
        s_observedNormalizedScale = default;
        s_observedNormalizedDeviceBounds = default;
        s_observedNormalizedRasterBounds = default;
        var effect = new WorkingScaleProbeEffect(static context =>
        {
            context.CustomEffect(
                s_runtimeBounds,
                static (bounds, execution) =>
                {
                    foreach (EffectTarget target in execution.Targets)
                        target.Bounds = bounds;
                },
                static (bounds, _) => bounds);
            context.Geometry(GeometryDescription.Create(
                static session =>
                {
                    s_observedNormalizedScale = session.Input.EffectiveScale;
                    s_observedNormalizedDeviceBounds = session.Input.DeviceBounds;
                    s_observedNormalizedRasterBounds = session.Input.RasterBounds;
                    session.Canvas.Use(session.Input.Draw);
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: "typed-suffix-physical-normalization"));
        });
        using EffectTargets targets = CreateSolidTargets(s_oversizedSourceBounds);

        ApplyDirect(effect, s_oversizedSourceBounds, targets);

        Rect physicalBounds = new(0.5f, 0, 16_384, 1);
        float expectedDensity = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
            physicalBounds,
            1);
        PixelRect expectedDeviceBounds = PixelRect.FromRect(physicalBounds, expectedDensity);
        Assert.Multiple(() =>
        {
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(expectedDensity, Is.LessThan(1));
            Assert.That(s_observedNormalizedScale, Is.EqualTo(EffectiveScale.At(expectedDensity)));
            Assert.That(s_observedNormalizedDeviceBounds, Is.EqualTo(expectedDeviceBounds));
            Assert.That(s_observedNormalizedDeviceBounds.Width,
                Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(
                s_observedNormalizedRasterBounds,
                Is.EqualTo(expectedDeviceBounds.ToRect(expectedDensity)));
            Assert.That(
                Contains(
                    s_observedNormalizedDeviceBounds,
                    PixelRect.FromRect(s_runtimeBounds, expectedDensity)),
                Is.True);
        });
    }

    private static TargetSnapshot ApplyBlur(Rect bounds)
    {
        using EffectTargets targets = CreatePatternTargets(bounds);
        return ApplyBlur(bounds, targets);
    }

    private static TargetSnapshot ApplyBlur(
        Rect bounds,
        RenderTarget backing,
        PixelRect deviceBounds)
    {
        using EffectTargets targets = CreateTargets(backing, bounds, deviceBounds);
        return ApplyBlur(bounds, targets);
    }

    private static TargetSnapshot ApplyBlur(Rect bounds, EffectTargets targets)
    {
        var effect = new WorkingScaleProbeEffect(static context => context.Blur(new Size(1, 1)));
        ApplyDirect(effect, bounds, targets);

        EffectTarget target = targets.Single();
        using Bitmap bitmap = target.RenderTarget!.Snapshot();
        return new TargetSnapshot(
            target.Bounds,
            target.DeviceBounds,
            target.RasterBounds,
            bitmap.GetPixelSpan<ushort>().ToArray());
    }

    private static void ApplyDirect(FilterEffect effect, Rect bounds, EffectTargets targets)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds, outputScale: 1, workingScale: 1);
        context.ApplyTransactional(effect, resource);
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);
        activator.Apply(context);
        activator.Flush(false);
    }

    private static EffectTargets CreatePatternTargets(Rect bounds)
    {
        PixelRect deviceBounds = PixelRect.FromRect(bounds, 1);
        using CpuRenderTarget renderTarget = CreatePatternRenderTarget(
            deviceBounds.Width,
            deviceBounds.Height);
        return CreateTargets(renderTarget, bounds, deviceBounds);
    }

    private static CpuRenderTarget CreatePatternRenderTarget(int width, int height)
    {
        var renderTarget = new CpuRenderTarget(width, height);
        SKCanvas canvas = renderTarget.Value.Canvas;
        canvas.Clear(SKColors.Transparent);
        using (var red = new SKPaint { Color = SKColors.Red })
        using (var blue = new SKPaint { Color = SKColors.Blue })
        {
            canvas.DrawRect(SKRect.Create(0, 0, 1, height), red);
            canvas.DrawRect(SKRect.Create(1, 0, width - 1, height), blue);
        }

        canvas.Flush();
        return renderTarget;
    }

    private static EffectTargets CreateTargets(
        RenderTarget backing,
        Rect bounds,
        PixelRect deviceBounds)
    {
        return new EffectTargets
        {
            new EffectTarget(backing, bounds, EffectiveScale.At(1), deviceBounds)
            {
                OriginalBounds = new Rect(default, bounds.Size),
            },
        };
    }

    private static EffectTargets CreateSolidTargets(Rect bounds)
    {
        PixelRect deviceBounds = PixelRect.FromRect(bounds, 1);
        using var renderTarget = new CpuRenderTarget(deviceBounds.Width, deviceBounds.Height);
        renderTarget.Value.Canvas.Clear(SKColors.White);
        renderTarget.Value.Canvas.Flush();
        return new EffectTargets
        {
            new EffectTarget(renderTarget, bounds, EffectiveScale.At(1), deviceBounds)
            {
                OriginalBounds = new Rect(default, bounds.Size),
            },
        };
    }

    private static bool Contains(PixelRect outer, PixelRect inner)
        => outer.X <= inner.X
           && outer.Y <= inner.Y
           && outer.Right >= inner.Right
           && outer.Bottom >= inner.Bottom;

    private sealed class MaterializedMetadataProbeNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            foreach (RenderFragmentHandle input in context.Inputs)
            {
                OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                    static session =>
                    {
                        RenderExecutionInput source = session.Inputs.Single();
                        s_observedMaterializedScale = source.EffectiveScale;
                        s_observedMaterializedDeviceBounds = source.DeviceBounds;
                        s_observedMaterializedRasterBounds = source.RasterBounds;
                        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                        output.Canvas.Use(source.Draw);
                        session.Publish(output);
                    },
                    RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
                    RenderHitTestContract.AnyInput,
                    RenderValueCardinality.Single,
                    RenderScaleContract.PreserveInputSupply,
                    structuralKey: typeof(MaterializedMetadataProbeNode));
                context.Publish(context.OpaqueMap(input, description));
            }
        }
    }

    private sealed class TrackingTargetFactory : IRenderTargetFactory
    {
        public List<PixelSize> Allocations { get; } = [];

        public RenderTarget Create(PixelSize deviceSize)
        {
            Allocations.Add(deviceSize);
            return new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
        }
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(CreateSurface(width, height), width, height)
    {
        private static SKSurface CreateSurface(int width, int height)
            => SKSurface.Create(new SKImageInfo(
                   width,
                   height,
                   SKColorType.RgbaF16,
                   SKAlphaType.Premul,
                   SKColorSpace.CreateSrgbLinear()))
               ?? throw new InvalidOperationException("A CPU test surface could not be created.");
    }

    private readonly record struct TargetSnapshot(
        Rect Bounds,
        PixelRect DeviceBounds,
        Rect RasterBounds,
        ushort[] Pixels);
}
