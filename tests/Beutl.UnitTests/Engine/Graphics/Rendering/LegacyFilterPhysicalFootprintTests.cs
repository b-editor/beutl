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
    public void RasterShaderMapping_DifferentScalesAndOrigins_MapsDestinationToSourcePixels()
    {
        const float destinationScale = 1.25f;
        const float sourceScale = 2f;
        var destinationRasterBounds = new Rect(10.4f, -3.2f, 8, 6);
        var sourceRasterBounds = new Rect(-1.5f, 4.25f, 5, 4);
        SKMatrix matrix = RasterShaderMapping.CreateLocalMatrix(
            destinationScale,
            sourceScale,
            destinationRasterBounds,
            sourceRasterBounds);
        var destinationPixel = new SKPoint(7.5f, 3.25f);

        float mappedSourceX = (destinationPixel.X - matrix.TransX) / matrix.ScaleX;
        float mappedSourceY = (destinationPixel.Y - matrix.TransY) / matrix.ScaleY;
        float expectedSourceX = destinationPixel.X * sourceScale / destinationScale
                                + (float)((destinationRasterBounds.X - sourceRasterBounds.X) * sourceScale);
        float expectedSourceY = destinationPixel.Y * sourceScale / destinationScale
                                + (float)((destinationRasterBounds.Y - sourceRasterBounds.Y) * sourceScale);

        Assert.Multiple(() =>
        {
            Assert.That(matrix.ScaleX, Is.EqualTo(destinationScale / sourceScale).Within(1e-6));
            Assert.That(matrix.ScaleY, Is.EqualTo(destinationScale / sourceScale).Within(1e-6));
            Assert.That(mappedSourceX, Is.EqualTo(expectedSourceX).Within(1e-5));
            Assert.That(mappedSourceY, Is.EqualTo(expectedSourceY).Within(1e-5));
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
    public void StrokeEffect_ApronBackedInput_MatchesTightBacking()
    {
        var bounds = new Rect(1, 1, 10, 10);
        using var tightBacking = new CpuRenderTarget(10, 10);
        tightBacking.Value.Canvas.Clear(SKColors.White);
        tightBacking.Value.Canvas.Flush();

        using var apronBacking = new CpuRenderTarget(12, 12);
        apronBacking.Value.Canvas.Clear(SKColors.Transparent);
        using (var paint = new SKPaint { Color = SKColors.White })
        {
            apronBacking.Value.Canvas.DrawRect(SKRect.Create(1, 1, 10, 10), paint);
        }
        apronBacking.Value.Canvas.Flush();

        TargetSnapshot tight = ApplyStroke(
            bounds,
            tightBacking,
            new PixelRect(1, 1, 10, 10));
        TargetSnapshot apron = ApplyStroke(
            bounds,
            apronBacking,
            new PixelRect(0, 0, 12, 12));

        Assert.Multiple(() =>
        {
            Assert.That(apron.Bounds, Is.EqualTo(tight.Bounds));
            Assert.That(apron.DeviceBounds, Is.EqualTo(tight.DeviceBounds));
            Assert.That(apron.RasterBounds, Is.EqualTo(tight.RasterBounds));
            Assert.That(apron.Pixels.SequenceEqual(tight.Pixels), Is.True,
                "the raster apron must not move the traced stroke relative to the source");
        });
    }

    [Test]
    public void InvertIdentity_ApronBackedInput_PreservesCompleteBacking()
    {
        var bounds = new Rect(1, 1, 10, 10);
        var deviceBounds = new PixelRect(0, 0, 12, 12);
        using var backing = new CpuRenderTarget(deviceBounds.Width, deviceBounds.Height);
        DrawTestContent(backing.Value.Canvas, 1, 1, 1, separatedParts: true);
        backing.Value.Canvas.Flush();
        using Bitmap before = backing.Snapshot();
        ushort[] expected = before.GetPixelSpan<ushort>().ToArray();
        using EffectTargets targets = CreateTargets(backing, bounds, deviceBounds);
        var effect = new Invert();
        effect.Amount.CurrentValue = 0;

        ApplyCustomDirect(effect, bounds, targets);

        EffectTarget actual = targets.Single();
        using Bitmap after = actual.RenderTarget!.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(actual.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(actual.RasterBounds, Is.EqualTo(deviceBounds.ToRect(1)));
            Assert.That(after.GetPixelSpan<ushort>().SequenceEqual(expected), Is.True,
                "a same-bounds SKSL effect must preserve and map the complete apron backing");
        });
    }

    [Test]
    public void ColorShiftZeroOffsets_ApronBackedInput_PreservesCompleteBacking()
    {
        var bounds = new Rect(1, 1, 10, 10);
        var deviceBounds = new PixelRect(0, 0, 12, 12);
        using var backing = new CpuRenderTarget(deviceBounds.Width, deviceBounds.Height);
        DrawTestContent(backing.Value.Canvas, 1, 1, 1, separatedParts: true);
        backing.Value.Canvas.Flush();
        using Bitmap before = backing.Snapshot();
        ushort[] expected = before.GetPixelSpan<ushort>().ToArray();
        using EffectTargets targets = CreateTargets(backing, bounds, deviceBounds);

        ApplyCustomDirect(new ColorShift(), bounds, targets);

        EffectTarget actual = targets.Single();
        using Bitmap after = actual.RenderTarget!.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(actual.Bounds, Is.EqualTo(bounds));
            Assert.That(actual.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(actual.RasterBounds, Is.EqualTo(deviceBounds.ToRect(1)));
            Assert.That(after.GetPixelSpan<ushort>().SequenceEqual(expected), Is.True,
                "zero offsets must take the same-bounds path and preserve the complete backing");
        });
    }

    [Test]
    public void SourceLessSkslScript_UsesActualFootprintScaleAndCoversCompleteBacking()
    {
        const string script =
            """
            uniform float width;
            uniform float height;
            uniform float2 iResolution;
            uniform float iScale;

            half4 main(float2 fragCoord) {
                if (width != 9.0 || height != 8.0 ||
                    iResolution.x != 9.0 || iResolution.y != 8.0 ||
                    iScale != 2.0) {
                    return half4(1.0, 0.0, 1.0, 1.0);
                }

                return fragCoord.x >= 8.0 && fragCoord.y >= 7.0
                    ? half4(1.0, 0.0, 0.0, 1.0)
                    : half4(0.0, 0.0, 1.0, 1.0);
            }
            """;
        var bounds = new Rect(0.25f, 0.5f, 4, 3);
        var deviceBounds = new PixelRect(-2, -1, 9, 8);
        using var backing = new CpuRenderTarget(deviceBounds.Width, deviceBounds.Height);
        backing.Value.Canvas.Clear(SKColors.Transparent);
        backing.Value.Canvas.Flush();
        using EffectTargets targets = CreateTargets(backing, bounds, deviceBounds, scale: 2);
        var effect = new SKSLScriptEffect();
        effect.Script.CurrentValue = script;

        ApplyCustomDirect(effect, bounds, targets, workingScale: 1);

        EffectTarget actual = targets.Single();
        using Bitmap bitmap = actual.RenderTarget!.Snapshot();
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        ushort one = BitConverter.HalfToUInt16Bits((Half)1);
        ushort[] firstPixel = pixels[..4].ToArray();
        ushort[] finalPixel = pixels[^4..].ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(actual.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(actual.Scale, Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(bitmap.Width, Is.EqualTo(9));
            Assert.That(bitmap.Height, Is.EqualTo(8));
            Assert.That(firstPixel, Is.EqualTo(new ushort[] { 0, 0, one, one }),
                "metadata mismatches must not route through the magenta failure branch");
            Assert.That(finalPixel, Is.EqualTo(new ushort[] { one, 0, 0, one }),
                "RenderToTarget must cover the final physical backing pixel");
        });
    }

    [Test]
    public void ScaleOneLegacyIdentityCustom_PreservesDirectReplayPixels()
    {
        ushort[] direct = RenderLegacyIdentity(applyEffect: false);
        ushort[] throughLegacyEffect = RenderLegacyIdentity(applyEffect: true);

        Assert.That(throughLegacyEffect.SequenceEqual(direct), Is.True,
            "a pixel-aligned scale-1 legacy output must not be Mitchell-resampled");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FlatShadow_ApronBackedInput_MatchesTightBacking(bool shadowOnly)
    {
        var effect = new FlatShadow();
        effect.Angle.CurrentValue = 30;
        effect.Length.CurrentValue = 4;
        effect.Brush.CurrentValue = Brushes.Red;
        effect.ShadowOnly.CurrentValue = shadowOnly;

        AssertApronEffectMatchesTight(effect);
    }

    [Test]
    public void FlatShadow_TargetScaleDifferentFromContext_MatchesTightBacking()
    {
        var effect = new FlatShadow();
        effect.Angle.CurrentValue = 30;
        effect.Length.CurrentValue = 4;
        effect.Brush.CurrentValue = Brushes.Red;

        AssertApronEffectMatchesTight(
            effect,
            sourceScale: 2,
            workingScale: 1,
            pixelTolerance: 0.001f);
    }

    [Test]
    public void PartsSplit_ApronBackedInput_MatchesTightBacking()
    {
        AssertApronEffectMatchesTight(
            new PartsSplitEffect(),
            separatedParts: true,
            minimumOutputCount: 2);
    }

    [Test]
    public void PartsSplit_TargetScaleDifferentFromContext_MatchesTightBacking()
    {
        AssertApronEffectMatchesTight(
            new PartsSplitEffect(),
            separatedParts: true,
            minimumOutputCount: 2,
            sourceScale: 2,
            workingScale: 1);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Clipping_ApronBackedInput_MatchesTightBacking(bool autoClip)
    {
        var effect = new Clipping();
        effect.AutoClip.CurrentValue = autoClip;
        if (!autoClip)
        {
            effect.Left.CurrentValue = 2;
            effect.Top.CurrentValue = 1;
            effect.Right.CurrentValue = 1;
            effect.Bottom.CurrentValue = 2;
        }

        AssertApronEffectMatchesTight(effect);
    }

    [Test]
    public void StrokeEffect_TargetScaleDifferentFromContext_MatchesTightBacking()
    {
        var pen = new Pen();
        pen.Thickness.CurrentValue = 2;
        pen.Brush.CurrentValue = Brushes.Red;
        var effect = new StrokeEffect();
        effect.Pen.CurrentValue = pen;

        AssertApronEffectMatchesTight(
            effect,
            sourceScale: 2,
            workingScale: 1,
            pixelTolerance: 0.001f);
    }

    [Test]
    public void ColorShift_ApronBackedInput_MatchesTightBacking()
    {
        var effect = new ColorShift();
        effect.RedOffset.CurrentValue = new PixelPoint(-2, 1);
        effect.GreenOffset.CurrentValue = new PixelPoint(1, -1);
        effect.BlueOffset.CurrentValue = new PixelPoint(2, 2);
        effect.AlphaOffset.CurrentValue = new PixelPoint(-1, -2);

        AssertApronEffectMatchesTight(
            effect,
            bounds: new Rect(1.25f, 2.5f, 10, 10));
    }

    [Test]
    public void ColorShift_MovedSource_IsTranslationEquivalent()
    {
        var allocationBounds = new Rect(5.25f, 6.5f, 10, 10);
        var translation = new Vector(20, 30);
        PixelRect deviceBounds = PixelRect.FromRect(allocationBounds, 1);
        using CpuRenderTarget backing = CreatePatternRenderTarget(
            deviceBounds.Width,
            deviceBounds.Height);
        var effect = new ColorShift();
        effect.RedOffset.CurrentValue = new PixelPoint(-2, 1);
        effect.GreenOffset.CurrentValue = new PixelPoint(1, -1);
        effect.BlueOffset.CurrentValue = new PixelPoint(2, 2);
        effect.AlphaOffset.CurrentValue = new PixelPoint(-1, -2);

        TargetSnapshot origin = ApplyMovedEffect(
            effect,
            allocationBounds,
            allocationBounds,
            backing,
            deviceBounds);
        TargetSnapshot translated = ApplyMovedEffect(
            effect,
            allocationBounds,
            allocationBounds.Translate(translation),
            backing,
            deviceBounds);

        Assert.Multiple(() =>
        {
            Assert.That(translated.Bounds, Is.EqualTo(origin.Bounds.Translate(translation)));
            Assert.That(translated.RasterBounds, Is.EqualTo(origin.RasterBounds.Translate(translation)));
            Assert.That(translated.Pixels.SequenceEqual(origin.Pixels), Is.True,
                "mapped SKSL input coordinates must follow current RasterBounds rather than immutable DeviceBounds");
        });
    }

    [Test]
    public void Mosaic_AbsoluteOrigin_ApronBackedInput_MatchesTightInterior()
    {
        var effect = new MosaicEffect();
        effect.TileSize.CurrentValue = new Size(3, 2);
        effect.Origin.CurrentValue = new RelativePoint(2, 1, RelativeUnit.Absolute);

        AssertSameBoundsEffectApronInteriorMatches(effect, inset: 2);
    }

    [TestCaseSource(nameof(DisplacementTransforms))]
    public void DisplacementMap_ApronBackedInput_MatchesTightInterior(
        DisplacementMapTransform transform)
    {
        var effect = new DisplacementMapEffect();
        effect.Transform.CurrentValue = transform;

        AssertSameBoundsEffectApronInteriorMatches(
            effect,
            inset: 3,
            pixelTolerance: 0.002f);
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

    private static TargetSnapshot ApplyStroke(
        Rect bounds,
        RenderTarget backing,
        PixelRect deviceBounds)
    {
        using EffectTargets targets = CreateTargets(backing, bounds, deviceBounds);
        var pen = new Pen();
        pen.Thickness.CurrentValue = 2;
        pen.Brush.CurrentValue = Brushes.Red;
        var effect = new StrokeEffect();
        effect.Pen.CurrentValue = pen;
        ApplyCustomDirect(effect, bounds, targets);

        EffectTarget target = targets.Single();
        using Bitmap bitmap = target.RenderTarget!.Snapshot();
        return new TargetSnapshot(
            target.Bounds,
            target.DeviceBounds,
            target.RasterBounds,
            bitmap.GetPixelSpan<ushort>().ToArray());
    }

    private static ushort[] RenderLegacyIdentity(bool applyEffect)
    {
        RenderNode source = new PixelExactLegacySourceNode();
        FilterEffect.Resource? resource = null;
        RenderNode[] stages;
        if (applyEffect)
        {
            var effect = new WorkingScaleProbeEffect(static context =>
                context.CustomEffect(
                    0,
                    static (_, _) => { },
                    static (_, bounds) => bounds));
            resource = effect.ToResource(CompositionContext.Default);
            stages = [source, new FilterEffectRenderNode(resource)];
        }
        else
        {
            stages = [source];
        }

        try
        {
            using var pipeline = ScaleRecordingTestHelper.Pipeline(stages);
            using var renderer = new RenderNodeRenderer(
                pipeline,
                new RenderNodeRendererOptions
                {
                    Intent = RenderIntent.Delivery,
                    TargetDomain = PixelExactLegacySourceNode.Frame,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    UseRenderCache = false,
                    TargetFactory = new TrackingTargetFactory(),
                });
            using var target = new CpuRenderTarget(
                (int)PixelExactLegacySourceNode.Frame.Width,
                (int)PixelExactLegacySourceNode.Frame.Height);
            using var canvas = new ImmediateCanvas(
                target,
                1,
                logicalSize: PixelExactLegacySourceNode.Frame.Size);
            canvas.Clear();
            renderer.Render(canvas);
            using Bitmap bitmap = target.Snapshot();
            return bitmap.GetPixelSpan<ushort>().ToArray();
        }
        finally
        {
            resource?.Dispose();
        }
    }

    private static void AssertApronEffectMatchesTight(
        FilterEffect effect,
        bool separatedParts = false,
        int minimumOutputCount = 1,
        float sourceScale = 1,
        float workingScale = 1,
        Rect? bounds = null,
        float pixelTolerance = 0)
    {
        Rect logicalBounds = bounds ?? new Rect(1, 1, 10, 10);
        PixelRect tightDeviceBounds = PixelRect.FromRect(logicalBounds, sourceScale);
        Rect tightRasterBounds = tightDeviceBounds.ToRect(sourceScale);
        float tightOffsetX = (float)((logicalBounds.X - tightRasterBounds.X) * sourceScale);
        float tightOffsetY = (float)((logicalBounds.Y - tightRasterBounds.Y) * sourceScale);
        using var tightBacking = new CpuRenderTarget(
            tightDeviceBounds.Width,
            tightDeviceBounds.Height);
        DrawTestContent(
            tightBacking.Value.Canvas,
            tightOffsetX,
            tightOffsetY,
            sourceScale,
            separatedParts);
        tightBacking.Value.Canvas.Flush();

        PixelRect apronDeviceBounds = RenderScaleUtilities.AddRasterApron(tightDeviceBounds);
        Rect apronRasterBounds = apronDeviceBounds.ToRect(sourceScale);
        float apronOffsetX = (float)((logicalBounds.X - apronRasterBounds.X) * sourceScale);
        float apronOffsetY = (float)((logicalBounds.Y - apronRasterBounds.Y) * sourceScale);
        using var apronBacking = new CpuRenderTarget(
            apronDeviceBounds.Width,
            apronDeviceBounds.Height);
        DrawTestContent(
            apronBacking.Value.Canvas,
            apronOffsetX,
            apronOffsetY,
            sourceScale,
            separatedParts);
        apronBacking.Value.Canvas.Flush();

        TargetSnapshot[] tight = ApplyEffect(
            effect,
            logicalBounds,
            tightBacking,
            tightDeviceBounds,
            sourceScale,
            workingScale);
        TargetSnapshot[] apron = ApplyEffect(
            effect,
            logicalBounds,
            apronBacking,
            apronDeviceBounds,
            sourceScale,
            workingScale);

        Assert.That(apron, Has.Length.EqualTo(tight.Length));
        Assert.That(tight, Has.Length.GreaterThanOrEqualTo(minimumOutputCount));
        Assert.Multiple(() =>
        {
            for (int index = 0; index < tight.Length; index++)
            {
                Assert.That(apron[index].Bounds, Is.EqualTo(tight[index].Bounds));
                Assert.That(apron[index].DeviceBounds, Is.EqualTo(tight[index].DeviceBounds));
                Assert.That(apron[index].RasterBounds, Is.EqualTo(tight[index].RasterBounds));
                Assert.That(
                    PixelsEqualWithin(tight[index].Pixels, apron[index].Pixels, pixelTolerance),
                    Is.True,
                    $"the raster apron moved effect output {index} relative to the source; "
                    + DescribePixelDifference(tight[index].Pixels, apron[index].Pixels));
            }
        });
    }

    private static void AssertSameBoundsEffectApronInteriorMatches(
        FilterEffect effect,
        int inset,
        float pixelTolerance = 0)
    {
        var bounds = new Rect(1, 1, 10, 10);
        PixelRect tightDeviceBounds = PixelRect.FromRect(bounds, 1);
        using var tightBacking = new CpuRenderTarget(
            tightDeviceBounds.Width,
            tightDeviceBounds.Height);
        DrawTestContent(tightBacking.Value.Canvas, 0, 0, 1, separatedParts: true);
        tightBacking.Value.Canvas.Flush();

        PixelRect apronDeviceBounds = RenderScaleUtilities.AddRasterApron(tightDeviceBounds);
        using var apronBacking = new CpuRenderTarget(
            apronDeviceBounds.Width,
            apronDeviceBounds.Height);
        DrawTestContent(apronBacking.Value.Canvas, 1, 1, 1, separatedParts: true);
        apronBacking.Value.Canvas.Flush();

        TargetSnapshot tight = ApplyEffect(
            effect,
            bounds,
            tightBacking,
            tightDeviceBounds).Single();
        TargetSnapshot apron = ApplyEffect(
            effect,
            bounds,
            apronBacking,
            apronDeviceBounds).Single();
        var comparisonRegion = new PixelRect(
            tight.DeviceBounds.X + inset,
            tight.DeviceBounds.Y + inset,
            tight.DeviceBounds.Width - inset * 2,
            tight.DeviceBounds.Height - inset * 2);

        bool equal = DeviceRegionEquals(
            tight,
            apron,
            comparisonRegion,
            pixelTolerance,
            out string difference);
        Assert.Multiple(() =>
        {
            Assert.That(tight.Bounds, Is.EqualTo(apron.Bounds));
            Assert.That(tight.DeviceBounds, Is.EqualTo(tightDeviceBounds));
            Assert.That(apron.DeviceBounds, Is.EqualTo(apronDeviceBounds));
            Assert.That(equal, Is.True, difference);
        });
    }

    private static void DrawTestContent(
        SKCanvas canvas,
        float offsetX,
        float offsetY,
        float scale,
        bool separatedParts)
    {
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = SKColors.White };
        if (separatedParts)
        {
            canvas.DrawRect(
                SKRect.Create(offsetX + scale, offsetY + scale, 3 * scale, 3 * scale),
                paint);
            canvas.DrawRect(
                SKRect.Create(offsetX + 6 * scale, offsetY + 6 * scale, 3 * scale, 3 * scale),
                paint);
        }
        else
        {
            canvas.DrawRect(SKRect.Create(offsetX, offsetY, 10 * scale, 10 * scale), paint);
        }
    }

    private static TargetSnapshot[] ApplyEffect(
        FilterEffect effect,
        Rect bounds,
        RenderTarget backing,
        PixelRect deviceBounds,
        float sourceScale = 1,
        float workingScale = 1)
    {
        using EffectTargets targets = CreateTargets(backing, bounds, deviceBounds, sourceScale);
        ApplyCustomDirect(effect, bounds, targets, workingScale);

        var result = new TargetSnapshot[targets.Count];
        for (int index = 0; index < targets.Count; index++)
        {
            EffectTarget target = targets[index];
            using Bitmap bitmap = target.RenderTarget!.Snapshot();
            result[index] = new TargetSnapshot(
                target.Bounds,
                target.DeviceBounds,
                target.RasterBounds,
                bitmap.GetPixelSpan<ushort>().ToArray());
        }

        return result;
    }

    private static TargetSnapshot ApplyMovedEffect(
        FilterEffect effect,
        Rect allocationBounds,
        Rect currentBounds,
        RenderTarget backing,
        PixelRect deviceBounds)
    {
        using EffectTargets targets = CreateTargets(backing, allocationBounds, deviceBounds);
        targets[0].Bounds = currentBounds;
        ApplyCustomDirect(effect, currentBounds, targets);

        EffectTarget target = targets.Single();
        using Bitmap bitmap = target.RenderTarget!.Snapshot();
        return new TargetSnapshot(
            target.Bounds,
            target.DeviceBounds,
            target.RasterBounds,
            bitmap.GetPixelSpan<ushort>().ToArray());
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

    private static void ApplyCustomDirect(
        FilterEffect effect,
        Rect bounds,
        EffectTargets targets,
        float workingScale = 1)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var recording = new FilterEffectContext(bounds, outputScale: 1, workingScale);
        recording.ApplyTransactional(effect, resource);
        IFEItem_Custom item = recording.GetOrderedItems().OfType<IFEItem_Custom>().Single();
        var execution = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale,
            maxWorkingScale: 1);
        item.Accepts(execution);
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
        PixelRect deviceBounds,
        float scale = 1)
    {
        return new EffectTargets
        {
            new EffectTarget(backing, bounds, EffectiveScale.At(scale), deviceBounds)
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

    private static string DescribePixelDifference(ushort[] expected, ushort[] actual)
    {
        int length = Math.Min(expected.Length, actual.Length);
        int first = -1;
        int count = Math.Abs(expected.Length - actual.Length);
        int maxDelta = 0;
        for (int index = 0; index < length; index++)
        {
            if (expected[index] == actual[index])
                continue;

            if (first < 0)
                first = index;
            count++;
            maxDelta = Math.Max(maxDelta, Math.Abs(expected[index] - actual[index]));
        }

        return $"expected length {expected.Length}, actual length {actual.Length}, "
               + $"differing values {count}, first difference {first}, "
               + $"first values {(first >= 0 ? expected[first] : 0)}/{(first >= 0 ? actual[first] : 0)}, "
               + $"max delta {maxDelta}";
    }

    private static bool PixelsEqualWithin(ushort[] expected, ushort[] actual, float tolerance)
    {
        if (expected.Length != actual.Length)
            return false;
        if (tolerance <= 0)
            return expected.SequenceEqual(actual);

        for (int index = 0; index < expected.Length; index++)
        {
            float expectedValue = (float)BitConverter.UInt16BitsToHalf(expected[index]);
            float actualValue = (float)BitConverter.UInt16BitsToHalf(actual[index]);
            if (MathF.Abs(expectedValue - actualValue) > tolerance)
                return false;
        }

        return true;
    }

    private static bool DeviceRegionEquals(
        TargetSnapshot expected,
        TargetSnapshot actual,
        PixelRect region,
        float tolerance,
        out string difference)
    {
        int differingValues = 0;
        float maxDelta = 0;
        PixelPoint firstPixel = default;
        int firstChannel = -1;
        for (int y = region.Y; y < region.Bottom; y++)
        {
            for (int x = region.X; x < region.Right; x++)
            {
                int expectedBase = ((y - expected.DeviceBounds.Y) * expected.DeviceBounds.Width
                                    + x - expected.DeviceBounds.X) * 4;
                int actualBase = ((y - actual.DeviceBounds.Y) * actual.DeviceBounds.Width
                                  + x - actual.DeviceBounds.X) * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    float expectedValue =
                        (float)BitConverter.UInt16BitsToHalf(expected.Pixels[expectedBase + channel]);
                    float actualValue =
                        (float)BitConverter.UInt16BitsToHalf(actual.Pixels[actualBase + channel]);
                    float delta = MathF.Abs(expectedValue - actualValue);
                    if (delta <= tolerance)
                        continue;

                    if (firstChannel < 0)
                    {
                        firstPixel = new PixelPoint(x, y);
                        firstChannel = channel;
                    }

                    differingValues++;
                    maxDelta = Math.Max(maxDelta, delta);
                }
            }
        }

        difference = $"device region {region} differs in {differingValues} channel values; "
                     + $"first pixel {firstPixel}, channel {firstChannel}, max delta {maxDelta}";
        return differingValues == 0;
    }

    private static IEnumerable<TestCaseData> DisplacementTransforms()
    {
        var translate = new DisplacementMapTranslateTransform();
        translate.X.CurrentValue = 1.5f;
        translate.Y.CurrentValue = -1;
        yield return new TestCaseData(translate).SetName("DisplacementMap_Apron_Translate");

        var scale = new DisplacementMapScaleTransform();
        scale.Scale.CurrentValue = 125;
        scale.ScaleX.CurrentValue = 120;
        scale.ScaleY.CurrentValue = 80;
        scale.CenterX.CurrentValue = 1.25f;
        scale.CenterY.CurrentValue = -0.75f;
        yield return new TestCaseData(scale).SetName("DisplacementMap_Apron_Scale");

        var rotation = new DisplacementMapRotationTransform();
        rotation.Rotation.CurrentValue = 18;
        rotation.CenterX.CurrentValue = 1.25f;
        rotation.CenterY.CurrentValue = -0.75f;
        yield return new TestCaseData(rotation).SetName("DisplacementMap_Apron_Rotation");
    }

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

    private sealed class PixelExactLegacySourceNode : RenderNode
    {
        public static readonly Rect Frame = new(0, 0, 20, 20);
        private static readonly Rect s_bounds = new(2, 2, 16, 16);

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.CreateEngineSource(
                execute: static session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(static canvas => Draw(canvas.Canvas));
                    session.Publish(output);
                },
                directReplay: static session => Draw(session.Canvas.Canvas),
                bounds: RenderOperationBoundsContract.Source(s_bounds),
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(PixelExactLegacySourceNode),
                runtimeIdentity: new RenderRuntimeIdentity(typeof(PixelExactLegacySourceNode)));
            context.Publish(context.OpaqueSource(description));
        }

        private static void Draw(SKCanvas canvas)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(225, 85, 30, 205),
            };
            canvas.DrawRoundRect(new SKRect(3.25f, 4.5f, 16.75f, 15.25f), 2.5f, 2.5f, paint);
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
