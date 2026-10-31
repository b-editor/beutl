using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class RadialBlurTests
{
    private static readonly Rect s_bounds = new(10, 20, 100, 60);

    [TestCase(RadialBlurMode.Zoom)]
    [TestCase(RadialBlurMode.Spin)]
    public void ApplyTo_RecordsOneShaderGraph(RadialBlurMode mode)
    {
        RadialBlur effect = Create(mode);
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);

        Assert.That(context.GetOrderedItems(), Has.Count.EqualTo(1));
        Assert.That(context.GetOrderedItems().Single(), Is.InstanceOf<FEItem_ShaderGraph>());
    }

    [TestCase(RadialBlurMode.Zoom, 0f)]
    [TestCase(RadialBlurMode.Spin, 0f)]
    [TestCase(RadialBlurMode.Zoom, -10f)]
    [TestCase(RadialBlurMode.Spin, float.NaN)]
    [TestCase(RadialBlurMode.Zoom, float.PositiveInfinity)]
    public void ZeroOrInvalidAmount_RecordsNothing(RadialBlurMode mode, float amount)
    {
        using var resource = new RadialBlur.Resource
        {
            Mode = mode,
            Amount = amount,
            Center = RelativePoint.Center,
            Samples = 32,
        };
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);

        Assert.Multiple(() =>
        {
            Assert.That(context.GetOrderedItems(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(s_bounds));
        });
    }

    [Test]
    public void NonFiniteCenter_RecordsNothing()
    {
        var effect = Create(RadialBlurMode.Zoom);
        effect.Center.Value = new RelativePoint(float.NaN, 0, RelativeUnit.Absolute);
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);

        Assert.That(context.GetOrderedItems(), Is.Empty);
    }

    [TestCase(-1, 2)]
    [TestCase(1, 2)]
    [TestCase(10000, 128)]
    public void DetachedSampleCount_IsClamped(int samples, int expected)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            (Bitmap reference, _) = RenderGpuMarker(Create(RadialBlurMode.Zoom, 90, expected), null, 1, default);
            using (reference)
            {
                (Bitmap actual, _) = RenderGpuMarker(Create(RadialBlurMode.Zoom, 90), null, 1, default,
                    samplesOverride: samples);
                using (actual)
                {
                    Assert.That(actual.GetPixelSpan<ushort>().ToArray(),
                        Is.EqualTo(reference.GetPixelSpan<ushort>().ToArray()));
                }
            }
        });
    }

    [TestCase(RelativeUnit.Relative)]
    [TestCase(RelativeUnit.Absolute)]
    public void Zoom_ExtendsBoundsAroundTheSelectedCenter(RelativeUnit unit)
    {
        var effect = Create(RadialBlurMode.Zoom, 100);
        effect.Center.Value = unit == RelativeUnit.Relative
            ? new RelativePoint(0.25f, 0.75f, unit)
            : new RelativePoint(25, 45, unit);

        Rect bounds = RecordBounds(effect, s_bounds);

        Assert.That(bounds, Is.EqualTo(new Rect(-15, -25, 200, 120)));
    }

    [Test]
    public void Spin_BoundsIncludeTheRotatedCorners()
    {
        Rect bounds = RecordBounds(Create(RadialBlurMode.Spin, 90, 3), s_bounds);
        float extent = 80 * MathF.Sqrt(2);

        Assert.Multiple(() =>
        {
            Assert.That(bounds.X, Is.EqualTo(60 - extent / 2).Within(0.001f));
            Assert.That(bounds.Y, Is.EqualTo(50 - extent / 2).Within(0.001f));
            Assert.That(bounds.Width, Is.EqualTo(extent).Within(0.001f));
            Assert.That(bounds.Height, Is.EqualTo(extent).Within(0.001f));
        });
    }

    [TestCase(RadialBlurMode.Zoom, 0.5f)]
    [TestCase(RadialBlurMode.Zoom, 2f)]
    [TestCase(RadialBlurMode.Spin, 0.5f)]
    [TestCase(RadialBlurMode.Spin, 2f)]
    public void Rendering_ResolvesRelativeAndAbsoluteCentersEqually(RadialBlurMode mode, float scale)
    {
        RadialBlur relative = Create(mode, 80, 64);
        relative.Center.Value = new RelativePoint(0.25f, 0.75f, RelativeUnit.Relative);
        RadialBlur absolute = Create(mode, 80, 64);
        absolute.Center.Value = new RelativePoint(10, 30, RelativeUnit.Absolute);
        var translation = new Vector(10, 20);

        using Bitmap expected = RenderMarker(relative, out Rect expectedBounds, scale: scale, translation: translation);
        using Bitmap actual = RenderMarker(absolute, out Rect actualBounds, scale: scale, translation: translation);

        Assert.That(actualBounds, Is.EqualTo(expectedBounds));
        Assert.That(actual.GetPixelSpan<ushort>().ToArray(), Is.EqualTo(expected.GetPixelSpan<ushort>().ToArray()));
    }

    [TestCase(RadialBlurMode.Zoom)]
    [TestCase(RadialBlurMode.Spin)]
    public void Serialization_RetainsAllSettingsInAnEffectGroup(RadialBlurMode mode)
    {
        var effect = Create(mode, 45, 64);
        effect.Center.Value = new RelativePoint(12, 8, RelativeUnit.Absolute);
        var group = new FilterEffectGroup();
        group.Children.Add(effect);
        var json = CoreSerializer.SerializeToJsonObject(group);
        var restored = (FilterEffectGroup)CoreSerializer.DeserializeFromJsonObject(json, typeof(FilterEffectGroup));
        var actual = (RadialBlur)restored.Children.Single();

        Assert.Multiple(() =>
        {
            Assert.That(actual.Mode.Value, Is.EqualTo(mode));
            Assert.That(actual.Amount.Value, Is.EqualTo(45));
            Assert.That(actual.Samples.Value, Is.EqualTo(64));
            Assert.That(actual.Center.Value, Is.EqualTo(effect.Center.Value));
        });
    }

    [Test]
    public void Zoom_RendersAnOutwardTrailBeyondTheInput()
    {
        using Bitmap bitmap = RenderMarker(Create(RadialBlurMode.Zoom, 100, 65), out Rect rasterBounds);

        Assert.Multiple(() =>
        {
            Assert.That(AlphaAt(bitmap, rasterBounds, 42, 20), Is.GreaterThan(0.02f));
            Assert.That(AlphaAt(bitmap, rasterBounds, 25, 20), Is.Zero);
            Assert.That(AlphaAt(bitmap, rasterBounds, 30, 10), Is.Zero);
        });
    }

    [Test]
    public void Spin_RendersAnArcInBothDirectionsWithoutZooming()
    {
        using Bitmap bitmap = RenderMarker(Create(RadialBlurMode.Spin, 90, 65), out Rect rasterBounds);

        Assert.Multiple(() =>
        {
            Assert.That(AlphaAt(bitmap, rasterBounds, 27, 12), Is.GreaterThan(0.02f));
            Assert.That(AlphaAt(bitmap, rasterBounds, 27, 27), Is.GreaterThan(0.02f));
            Assert.That(AlphaAt(bitmap, rasterBounds, 38, 20), Is.Zero);
            Assert.That(AlphaAt(bitmap, rasterBounds, 20, 20), Is.Zero);
        });
    }

    [TestCase(RadialBlurMode.Zoom, 0.5f)]
    [TestCase(RadialBlurMode.Zoom, 2f)]
    [TestCase(RadialBlurMode.Spin, 0.5f)]
    [TestCase(RadialBlurMode.Spin, 2f)]
    public void Rendering_PreservesTheTrailAtDifferentScalesAndTranslations(RadialBlurMode mode, float scale)
    {
        var translation = new Vector(10, 20);
        using Bitmap original = RenderMarker(Create(mode, 90, 65), out _, scale: scale);
        using Bitmap shifted = RenderMarker(Create(mode, 90, 65), out Rect raster, scale: scale, translation: translation);
        Assert.That(shifted.GetPixelSpan<ushort>().ToArray(), Is.EqualTo(original.GetPixelSpan<ushort>().ToArray()),
            "moving the input and its center together must preserve the rendered pixels");

        int x = mode == RadialBlurMode.Zoom ? 40 : 27;
        int y = mode == RadialBlurMode.Zoom ? 20 : 27;
        Assert.That(AlphaAt(shifted, raster, x + 10, y + 20, scale), Is.GreaterThan(0.02f),
            "the trail must remain at the same logical position at every working scale");
    }

    [TestCase(RadialBlurMode.Zoom)]
    [TestCase(RadialBlurMode.Spin)]
    public void Rendering_AveragesPremultipliedColorAndAlpha(RadialBlurMode mode)
    {
        using Bitmap bitmap = RenderMarker(Create(mode, 90, 65), out _, new SKColor(255, 0, 0, 128));
        ushort[] channels = bitmap.GetPixelSpan<ushort>().ToArray();
        float maxAlpha = 0;
        for (int i = 0; i < channels.Length; i += 4)
        {
            float red = (float)BitConverter.UInt16BitsToHalf(channels[i]);
            float alpha = (float)BitConverter.UInt16BitsToHalf(channels[i + 3]);
            Assert.That(red, Is.EqualTo(alpha).Within(0.002f), "premultiplied red must fade with coverage");
            Assert.That(alpha, Is.InRange(0, 128f / 255 + 0.002f));
            maxAlpha = MathF.Max(maxAlpha, alpha);
        }

        Assert.That(maxAlpha, Is.GreaterThan(0.05f), "the shader must actually render the source");
    }

    [TestCase(RadialBlurMode.Zoom, 34, 20)]
    [TestCase(RadialBlurMode.Spin, 30, 20)]
    public void SampleCount_ControlsTheTrailDistribution(RadialBlurMode mode, int x, int y)
    {
        using Bitmap sparse = RenderMarker(Create(mode, 100, 2), out Rect sparseBounds);
        using Bitmap dense = RenderMarker(Create(mode, 100, 65), out Rect denseBounds);

        Assert.That(AlphaAt(sparse, sparseBounds, x, y), Is.Zero);
        Assert.That(AlphaAt(dense, denseBounds, x, y), Is.GreaterThan(0.02f));
    }

    [TestCase(RadialBlurMode.Zoom)]
    [TestCase(RadialBlurMode.Spin)]
    public void RenderGraph_DrawsAndSelectsTheExpandedTrail(RadialBlurMode mode)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var content = new Rect(40, 40, 20, 10);
            RadialBlur Effect() => Create(mode, 90, 65);
            using Bitmap bitmap = FilteredShapeHarness.Render(content, Effect(), fullBleed: true);
            int x = mode == RadialBlurMode.Zoom ? 65 : 55;
            int y = mode == RadialBlurMode.Zoom ? 45 : 52;
            Assert.Multiple(() =>
            {
                Assert.That(AlphaAt(bitmap, new Rect(0, 0, 100, 100), x, y), Is.GreaterThan(0.01f));
                Assert.That(FilteredShapeHarness.HitTest(content, Effect(), new Point(x + 0.5f, y + 0.5f), fullBleed: true), Is.True);
            });
        });
    }

    [TestCase(RadialBlurMode.Zoom, 2f, 1f)]
    [TestCase(RadialBlurMode.Spin, 2f, 1f)]
    [TestCase(RadialBlurMode.Zoom, 1f, 2f)]
    [TestCase(RadialBlurMode.Spin, 1f, 2f)]
    public void ShaderGraph_SamplesInputAtADifferentOutputScale(RadialBlurMode mode, float inputScale, float outputScale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            (Bitmap reference, Rect expectedBounds) = RenderGpuMarker(Create(mode, 40), null, outputScale, default,
                invokeGraphDirectly: true);
            using (reference)
            {
                (Bitmap actual, Rect actualBounds) = RenderGpuMarker(Create(mode, 40), null, inputScale, default, outputScale,
                    invokeGraphDirectly: true);
                using (actual)
                {
                    Assert.That(actualBounds, Is.EqualTo(expectedBounds));
                    Assert.That((actual.Width, actual.Height), Is.EqualTo((reference.Width, reference.Height)));
                    Assert.That(actual.GetPixelSpan<ushort>().ToArray(), Is.EqualTo(reference.GetPixelSpan<ushort>().ToArray()));
                }
            }
        });
    }

    [TestCase(RadialBlurMode.Zoom, 0.5f, RelativeUnit.Relative)]
    [TestCase(RadialBlurMode.Spin, 0.5f, RelativeUnit.Relative)]
    [TestCase(RadialBlurMode.Zoom, 1.5f, RelativeUnit.Relative)]
    [TestCase(RadialBlurMode.Spin, 1.5f, RelativeUnit.Relative)]
    [TestCase(RadialBlurMode.Zoom, 50f, RelativeUnit.Absolute)]
    [TestCase(RadialBlurMode.Spin, 50f, RelativeUnit.Absolute)]
    [TestCase(RadialBlurMode.Zoom, 0.5f, RelativeUnit.Relative, true)]
    [TestCase(RadialBlurMode.Spin, 0.5f, RelativeUnit.Relative, true)]
    public void FragmentedInputs_UseOneCenterForBoundsAndSampling(RadialBlurMode mode, float centerX, RelativeUnit unit, bool opaque = false)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget backing = RenderTarget.Create(10, 10)!;
            backing.Value.Canvas.Clear(SKColors.White);
            backing.Value.Canvas.Flush();
            using var targets = new EffectTargets
            {
                new EffectTarget(backing, new Rect(0, 0, 10, 10), EffectiveScale.At(1)),
                new EffectTarget(backing, new Rect(90, 0, 10, 10), EffectiveScale.At(1)),
            };
            RadialBlur effect = Create(mode, mode == RadialBlurMode.Zoom ? 100 : 90, 65);
            effect.Center.Value = new RelativePoint(centerX, unit == RelativeUnit.Relative ? 0.5f : 5, unit);
            using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
            using var context = new FilterEffectContext(targets.CalculateBounds());
            Rect expectedBounds = RecordBounds(effect, targets.CalculateBounds());
            if (opaque)
                context.CustomEffect(0, static (_, execution) =>
                {
                    foreach (EffectTarget target in execution.Targets)
                        target.Bounds = target.Bounds.Translate(new Vector(100, 100));
                });
            context.ApplyTransactional(resource);
            if (opaque)
                expectedBounds = expectedBounds.Translate(new Vector(100, 100));
            using var builder = new SKImageFilterBuilder();
            using var executor = new FilterEffectExecutor(targets, builder, RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary, drawableBrushMaterializer: null);
            executor.Apply(context);
            executor.Flush(false);

            Assert.That(targets, Has.Count.EqualTo(2));
            Rect actual = targets.CalculateBounds();
            Assert.Multiple(() =>
            {
                Assert.That(actual.X, Is.EqualTo(expectedBounds.X).Within(0.001f));
                Assert.That(actual.Y, Is.EqualTo(expectedBounds.Y).Within(0.001f));
                Assert.That(actual.Right, Is.EqualTo(expectedBounds.Right).Within(0.001f));
                Assert.That(actual.Bottom, Is.EqualTo(expectedBounds.Bottom).Within(0.001f));
            });

            float center = unit == RelativeUnit.Relative ? centerX * 100 : centerX;
            for (int i = 0; i < targets.Count; i++)
            {
                float offset = 5 + i * 90 - center;
                Point trail = mode == RadialBlurMode.Zoom
                    ? new Point(center + offset * 1.5f, 5)
                    : new Point(center + offset * MathF.Cos(MathF.PI / 6), 5 - offset * MathF.Sin(MathF.PI / 6));
                if (opaque) trail += new Vector(100, 100);
                using Bitmap bitmap = targets[i].RenderTarget!.Snapshot();
                Assert.That(AlphaAt(bitmap, targets[i].RasterBounds, (int)MathF.Floor(trail.X), (int)MathF.Floor(trail.Y)),
                    Is.GreaterThan(0.02f), "Both shapes must leave trails around the group's center.");
            }
        });
    }

    private static Rect RecordBounds(RadialBlur effect, Rect bounds)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(resource);
        return context.Bounds;
    }

    private static RadialBlur Create(RadialBlurMode mode, float amount = 10, int samples = 32)
        => new()
        {
            Mode = { Value = mode },
            Amount = { Value = amount },
            Samples = { Value = samples },
        };

    private static Bitmap RenderMarker(
        RadialBlur effect, out Rect rasterBounds, SKColor? color = null, float scale = 1, Vector translation = default)
    {
        VulkanTestEnvironment.EnsureAvailable();
        (Bitmap bitmap, Rect bounds) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
            RenderGpuMarker(effect, color, scale, translation));
        rasterBounds = bounds;
        return bitmap;
    }

    private static (Bitmap, Rect) RenderGpuMarker(
        RadialBlur effect, SKColor? color, float scale, Vector translation, float? outputScale = null,
        int? samplesOverride = null, bool invokeGraphDirectly = false)
    {
        var bounds = new Rect(translation.X, translation.Y, 40, 40);
        using RenderTarget backing = RenderTarget.Create((int)(40 * scale), (int)(40 * scale))!;
        backing.Value.Canvas.Clear(SKColors.Transparent);
        backing.Value.Canvas.Scale(scale);
        using (var paint = new SKPaint { Color = color ?? SKColors.White })
        {
            backing.Value.Canvas.DrawRect(28, 18, 4, 4, paint);
        }
        backing.Value.Canvas.Flush();
        using var targets = new EffectTargets
        {
            new EffectTarget(backing, bounds, EffectiveScale.At(scale), PixelRect.FromRect(bounds, scale)),
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        if (samplesOverride is { } samples)
            ((RadialBlur.Resource)resource).Samples = samples;
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(resource);
        if (invokeGraphDirectly)
        {
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            FilterEffectStageFallbackExecutor.ApplyShaderGraph(targets,
                ((FEItem_ShaderGraph)context.GetOrderedItems().Single()).Description,
                outputScale ?? scale, outputScale ?? scale, outputScale ?? scale,
                RenderIntent.Preview, RenderRequestPurpose.Auxiliary, null, BufferDimensionBudget.EngineCeiling,
                (device, pass, identity) => ShaderGraphPipeline.Acquire(cache,
                    new ProgramCacheContextKey(cache, device, "test", "test"), device, pass, identity));
        }
        else
        {
            using var builder = new SKImageFilterBuilder();
            using var executor = new FilterEffectExecutor(
                targets, builder, RenderIntent.Preview, RenderRequestPurpose.Auxiliary,
                drawableBrushMaterializer: null, outputScale: outputScale ?? scale,
                workingScale: outputScale ?? scale, maxWorkingScale: outputScale ?? scale);
            executor.Apply(context);
            executor.Flush(false);
        }

        EffectTarget result = targets.Single();
        return (result.RenderTarget!.Snapshot(), result.RasterBounds);
    }

    private static float AlphaAt(Bitmap bitmap, Rect rasterBounds, int x, int y, float scale = 1)
    {
        int localX = (int)((x + 0.5f - rasterBounds.X) * scale);
        int localY = (int)((y + 0.5f - rasterBounds.Y) * scale);
        return (float)BitConverter.UInt16BitsToHalf(bitmap.GetRow<ushort>(localY)[localX * 4 + 3]);
    }
}
