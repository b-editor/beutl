using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Pixel;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
[NonParallelizable]
public sealed class ShaderMigrationPhysicalFootprintTests
{
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
    public void SkslScript_SourceSamplingOutsideInputPreservesClampEdges()
    {
        const string script =
            """
            uniform shader src;

            half4 main(float2 fragCoord) {
                return src.eval(float2(-1.0, 0.0));
            }
            """;
        var bounds = new Rect(0, 0, 2, 1);
        PixelRect deviceBounds = PixelRect.FromRect(bounds, 1);
        using CpuRenderTarget backing = CreatePatternRenderTarget(
            deviceBounds.Width,
            deviceBounds.Height);
        using EffectTargets targets = CreateTargets(backing, bounds, deviceBounds);
        var effect = new SKSLScriptEffect();
        effect.Script.CurrentValue = script;

        ApplyCustomDirect(effect, bounds, targets, workingScale: 1);

        EffectTarget actual = targets.Single();
        using Bitmap bitmap = actual.RenderTarget!.Snapshot();
        RgbaF16[] pixels = bitmap.GetPixelSpan<RgbaF16>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(pixels, Has.Length.EqualTo(2));
            Assert.That(pixels.Select(static pixel => (float)pixel.R), Is.All.EqualTo(1).Within(0.01f));
            Assert.That(pixels.Select(static pixel => (float)pixel.G), Is.All.EqualTo(0).Within(0.01f));
            Assert.That(pixels.Select(static pixel => (float)pixel.B), Is.All.EqualTo(0).Within(0.01f));
            Assert.That(pixels.Select(static pixel => (float)pixel.A), Is.All.EqualTo(1).Within(0.01f),
                "out-of-bounds script samples must repeat the nearest source edge");
        });
    }

    [Test]
    public void InvertIdentity_ApronBackedInput_PreservesSemanticPixels()
    {
        var bounds = new Rect(1, 1, 10, 10);
        PixelRect tightDeviceBounds = PixelRect.FromRect(bounds, 1);
        using var tightBacking = new CpuRenderTarget(
            tightDeviceBounds.Width,
            tightDeviceBounds.Height);
        DrawSeparatedContent(tightBacking.Value.Canvas, 0, 0);

        PixelRect apronDeviceBounds = RenderScaleUtilities.AddRasterApron(tightDeviceBounds);
        using var apronBacking = new CpuRenderTarget(
            apronDeviceBounds.Width,
            apronDeviceBounds.Height);
        DrawSeparatedContent(apronBacking.Value.Canvas, 1, 1);

        var effect = new Invert();
        effect.Amount.CurrentValue = 0;
        TargetSnapshot tight = ApplyEffect(effect, bounds, tightBacking, tightDeviceBounds);
        TargetSnapshot apron = ApplyEffect(effect, bounds, apronBacking, apronDeviceBounds);

        Assert.Multiple(() =>
        {
            AssertFiniteVisiblePixels(tight.Pixels);
            Assert.That(apron.Bounds, Is.EqualTo(tight.Bounds));
            Assert.That(apron.DeviceBounds, Is.EqualTo(tightDeviceBounds));
            Assert.That(apron.RasterBounds, Is.EqualTo(tight.RasterBounds));
            Assert.That(apron.Pixels.SequenceEqual(tight.Pixels), Is.True,
                "an identity CurrentPixel stage must discard only the physical apron");
        });
    }

    private static void AssertFiniteVisiblePixels(ushort[] pixels)
    {
        Assert.That(pixels, Is.Not.Empty);
        Assert.That(pixels.Length % 4, Is.Zero);
        bool hasVisiblePixel = false;
        for (int i = 0; i < pixels.Length; i++)
        {
            float channel = (float)BitConverter.UInt16BitsToHalf(pixels[i]);
            Assert.That(float.IsFinite(channel), Is.True,
                $"pixel channel {i} must be finite before apron parity is accepted");
            if ((i % 4) == 3 && channel > 0)
                hasVisiblePixel = true;
        }

        Assert.That(hasVisiblePixel, Is.True,
            "the tight apron-parity fixture must retain visible source content");
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
            Assert.That(origin.Pixels, Has.Some.Not.Zero);
            Assert.That(translated.Bounds, Is.EqualTo(origin.Bounds.Translate(translation)));
            Assert.That(translated.RasterBounds, Is.EqualTo(origin.RasterBounds.Translate(translation)));
            Assert.That(translated.Pixels.SequenceEqual(origin.Pixels), Is.True,
                "mapped SKSL input coordinates must follow current RasterBounds rather than immutable DeviceBounds");
        });
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
        ApplyDirect(effect, currentBounds, targets);

        return Snapshot(targets.Single());
    }

    private static TargetSnapshot ApplyEffect(
        FilterEffect effect,
        Rect bounds,
        RenderTarget backing,
        PixelRect deviceBounds)
    {
        using EffectTargets targets = CreateTargets(backing, bounds, deviceBounds);
        ApplyDirect(effect, bounds, targets);
        return Snapshot(targets.Single());
    }

    private static TargetSnapshot Snapshot(EffectTarget target)
    {
        using Bitmap bitmap = target.RenderTarget!.Snapshot();
        return new TargetSnapshot(
            target.Bounds,
            target.DeviceBounds,
            target.RasterBounds,
            bitmap.GetPixelSpan<ushort>().ToArray());
    }

    private static void ApplyDirect(
        FilterEffect effect,
        Rect bounds,
        EffectTargets targets,
        float workingScale = 1)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds, outputScale: 1, workingScale);
        context.ApplyTransactional(effect, resource);
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale,
            maxWorkingScale: 1);
        activator.Apply(context);
        activator.Flush(false);
    }

    private static void ApplyCustomDirect(
        FilterEffect effect,
        Rect bounds,
        EffectTargets targets,
        float workingScale)
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

    private static void DrawSeparatedContent(SKCanvas canvas, float offsetX, float offsetY)
    {
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(SKRect.Create(offsetX + 1, offsetY + 1, 3, 3), paint);
        canvas.DrawRect(SKRect.Create(offsetX + 6, offsetY + 6, 3, 3), paint);
        canvas.Flush();
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
