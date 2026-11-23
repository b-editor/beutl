using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class LensChromaticAberrationTests
{
    private static readonly Rect s_bounds = new(10, 20, 100, 60);
    private static readonly PixelSize s_patternSize = new(101, 21);

    [Test]
    public void SeparationGrowsAwayFromTheCenter()
    {
        using Bitmap bitmap = RenderPattern(10f, SKColors.White, out Rect raster);

        Rgba At(int x) => PixelAt(bitmap, x, 10, raster);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(At(50), Is.EqualTo(new Rgba(1, 1, 1, 1)));
            Assert.That(At(6), Is.EqualTo(new Rgba(1, 0, 0, 1)));
            Assert.That(At(10), Is.EqualTo(new Rgba(0, 1, 0, 1)));
            Assert.That(At(14), Is.EqualTo(new Rgba(0, 0, 1, 1)));
            Assert.That(At(86), Is.EqualTo(new Rgba(0, 0, 1, 1)));
            Assert.That(At(90), Is.EqualTo(new Rgba(0, 1, 0, 1)));
            Assert.That(At(94), Is.EqualTo(new Rgba(1, 0, 0, 1)));
            // Red reads between the transparent texel at x=88 and the white texel at x=89.
            Assert.That(At(92).Red, Is.EqualTo(2f / 11f).Within(0.01f));
        }
    }

    [Test]
    public void NegativeAmountSwapsRedAndBlueFringes()
    {
        using Bitmap bitmap = RenderPattern(-10f, SKColors.White, out Rect raster);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(PixelAt(bitmap, 86, 10, raster), Is.EqualTo(new Rgba(1, 0, 0, 1)));
            Assert.That(PixelAt(bitmap, 90, 10, raster), Is.EqualTo(new Rgba(0, 1, 0, 1)));
            Assert.That(PixelAt(bitmap, 94, 10, raster), Is.EqualTo(new Rgba(0, 0, 1, 1)));
        }
    }

    [Test]
    public void ZeroAmountIsAnExactPassThrough()
    {
        using var resource = new LensChromaticAberration.Resource { Amount = 0f, Center = RelativePoint.Center };
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnTarget(
            resource, s_patternSize, canvas => DrawPattern(canvas, new SKColor(40, 100, 150, 96)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(after, Is.EqualTo(before));
            AssertNoOp(resource);
        }
    }

    [TestCase(float.NaN, 0.5f, 0.5f)]
    [TestCase(float.PositiveInfinity, 0.5f, 0.5f)]
    [TestCase(float.NegativeInfinity, 0.5f, 0.5f)]
    [TestCase(10f, float.NaN, 0.5f)]
    [TestCase(10f, 0.5f, float.PositiveInfinity)]
    public void NonFiniteValuesRecordNothing(float amount, float centerX, float centerY)
    {
        using var resource = new LensChromaticAberration.Resource
        {
            Amount = amount,
            Center = new RelativePoint(centerX, centerY, RelativeUnit.Relative),
        };

        AssertNoOp(resource);
    }

    [TestCase(0.5f, 0.5f, 5f, 17f)]
    [TestCase(0.25f, 0.75f, 7.5f, 15.5f)]
    public void BoundsExpandAroundTheSelectedCenter(float centerX, float centerY, float left, float top)
    {
        using var resource = new LensChromaticAberration.Resource
        {
            Amount = 10f,
            Center = new RelativePoint(centerX, centerY, RelativeUnit.Relative),
        };
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Bounds.X, Is.EqualTo(left).Within(0.001f));
            Assert.That(context.Bounds.Y, Is.EqualTo(top).Within(0.001f));
            Assert.That(context.Bounds.Width, Is.EqualTo(110f).Within(0.001f));
            Assert.That(context.Bounds.Height, Is.EqualTo(66f).Within(0.001f));
        }
    }

    [TestCase(-40f)]
    [TestCase(40f)]
    public void DetachedAmountIsClamped(float amount)
    {
        using var resource = new LensChromaticAberration.Resource { Amount = amount, Center = RelativePoint.Center };
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);

        Assert.That(context.Bounds.Width, Is.EqualTo(110f).Within(0.001f));
    }

    [Test]
    public void SmallObjectAwayFromTheCenterKeepsBothFringes()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var content = new Rect(65, 45, 8, 10);
            var effect = new LensChromaticAberration
            {
                Amount = { Value = 10f },
                Center = { Value = new RelativePoint(-55, 5, RelativeUnit.Absolute) },
            };
            using Bitmap bitmap = FilteredShapeHarness.Render(content, effect, fullBleed: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(PixelAt(bitmap, 61, 50), Is.EqualTo(new Rgba(0, 0, 1, 1)));
                Assert.That(PixelAt(bitmap, 76, 50), Is.EqualTo(new Rgba(1, 0, 0, 1)));
                Assert.That(PixelAt(bitmap, 58, 50), Is.EqualTo(default(Rgba)));
                Assert.That(PixelAt(bitmap, 81, 50), Is.EqualTo(default(Rgba)));
            }
        });
    }

    [Test]
    public void OutputRemainsPremultiplied()
    {
        using Bitmap bitmap = RenderPattern(10f, new SKColor(255, 255, 255, 128), out Rect raster);

        Rgba redFringe = PixelAt(bitmap, 94, 10, raster);
        Assert.That(redFringe.Red, Is.EqualTo(128f / 255f).Within(0.002f));

        ReadOnlySpan<ushort> channels = bitmap.GetPixelSpan<ushort>();
        for (int i = 0; i < channels.Length; i += 4)
        {
            Rgba pixel = ReadPixel(channels[i..]);
            float maxChannel = MathF.Max(pixel.Red, MathF.Max(pixel.Green, pixel.Blue));
            Assert.That(maxChannel, Is.LessThanOrEqualTo(pixel.Alpha), $"pixel {i / 4}");
        }
    }

    private static void AssertNoOp(LensChromaticAberration.Resource resource)
    {
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.GetOrderedItems(), Is.Empty);
            Assert.That(context.Bounds, Is.EqualTo(s_bounds));
        }
    }

    private static Bitmap RenderPattern(float amount, SKColor color, out Rect rasterBounds)
    {
        VulkanTestEnvironment.EnsureAvailable();
        (Bitmap bitmap, Rect raster) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget backing = RenderTarget.Create(s_patternSize.Width, s_patternSize.Height)!;
            DrawPattern(backing.Value.Canvas, color);
            backing.Value.Canvas.Flush();
            var bounds = new Rect(0, 0, s_patternSize.Width, s_patternSize.Height);
            using var targets = new EffectTargets
            {
                new EffectTarget(backing, bounds, EffectiveScale.At(1), PixelRect.FromRect(bounds, 1)),
            };
            var effect = new LensChromaticAberration { Amount = { Value = amount } };
            using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
            using var context = new FilterEffectContext(bounds);
            context.ApplyTransactional(resource);
            using var builder = new SKImageFilterBuilder();
            using var executor = new FilterEffectExecutor(targets, builder, RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary, drawableBrushMaterializer: null);
            executor.Apply(context);
            executor.Flush(false);

            EffectTarget result = targets.Single();
            return (result.RenderTarget!.Snapshot(), result.Placement.RasterBounds);
        });
        rasterBounds = raster;
        return bitmap;
    }

    private static void DrawPattern(SKCanvas canvas, SKColor color)
    {
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = color };
        canvas.DrawRect(0, 0, s_patternSize.Width, 3, paint);
        foreach (int x in new[] { 9, 49, 89 })
            canvas.DrawRect(x, 3, 3, s_patternSize.Height - 3, paint);
    }

    private static Rgba PixelAt(Bitmap bitmap, int x, int y, Rect raster = default)
        => ReadPixel(bitmap.GetRow<ushort>(y - (int)raster.Y)[((x - (int)raster.X) * 4)..]);

    private static Rgba ReadPixel(ReadOnlySpan<ushort> channels)
        => new(
            (float)BitConverter.UInt16BitsToHalf(channels[0]),
            (float)BitConverter.UInt16BitsToHalf(channels[1]),
            (float)BitConverter.UInt16BitsToHalf(channels[2]),
            (float)BitConverter.UInt16BitsToHalf(channels[3]));
}
