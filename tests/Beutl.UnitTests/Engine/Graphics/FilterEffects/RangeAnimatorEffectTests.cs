using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class RangeAnimatorEffectTests
{
    [Test]
    public void DefaultValuesRecordNoEffect()
    {
        using FilterEffect.Resource resource = new RangeAnimatorEffect().ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, 40, 24));

        context.ApplyTransactional(resource);

        Assert.That(context.GetOrderedItems(), Is.Empty);
    }

    [TestCase(25f, 1f)]
    [TestCase(12.5f, 0.5f)]
    public void SelectedPartBlendsEveryValueAndOtherPartsKeepTheirPixels(float end, float amount)
    {
        using var targets = new EffectTargets
        {
            CreatePart(new Rect(19, 23, 40, 24)),
            CreatePart(new Rect(90.25f, 45.75f, 20, 18)),
            CreatePart(new Rect(120.75f, 48.25f, 30, 14)),
            CreatePart(new Rect(175.5f, 40.5f, 26, 22)),
        };
        EffectTarget[] unselected = targets.Skip(1).ToArray();
        Rect[] originalBounds = unselected.Select(target => target.Bounds).ToArray();
        ushort[][] originalPixels = unselected.Select(ReadBits).ToArray();
        var effect = new RangeAnimatorEffect
        {
            RangeStart = { Value = 25 },
            RangeEnd = { Value = 25 + end },
            RangeOffset = { Value = -25 },
            Position = { Value = new Point(16, -8) },
            Scale = { Value = 200 },
            Rotation = { Value = 180 },
            Opacity = { Value = 50 },
            Blur = { Value = 2 },
            Color = { Value = Color.FromArgb(0, 64, 128, 192) },
            ColorAmount = { Value = 80 },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);

        FilterEffectTestHarness.ApplyToTargets(resource, targets);

        Assert.That(targets.Count, Is.EqualTo(4));
        EffectTarget selected = targets[0];
        bool full = amount == 1;
        Point center = full ? new Point(55, 27) : new Point(47, 31);
        Size transformed = full ? new Size(80, 48) : new Size(36, 60);
        float blurMargin = 6 * amount;
        Point redSide = center + (full ? new Vector(20, 0) : new Vector(0, -15));
        Point justOutside = center + new Vector(transformed.Width / 2 + 0.5f, 0);
        using Bitmap bitmap = selected.RenderTarget!.Snapshot();
        float alpha = 0.8f * (1 - 0.5f * amount);
        float tintAmount = 0.8f * amount;
        float[] expectedRed =
        [
            alpha * (1 - tintAmount + tintAmount * 0.051269f),
            alpha * tintAmount * 0.215861f,
            alpha * tintAmount * 0.527115f,
            alpha,
        ];

        Assert.Multiple(() =>
        {
            Assert.That(selected.Bounds.Center.X, Is.EqualTo(center.X).Within(0.001f), "horizontal position");
            Assert.That(selected.Bounds.Center.Y, Is.EqualTo(center.Y).Within(0.001f), "vertical position");
            Assert.That(selected.Bounds.Width, Is.EqualTo(transformed.Width + 2 * blurMargin).Within(0.001f), "scaled, rotated and blurred width");
            Assert.That(selected.Bounds.Height, Is.EqualTo(transformed.Height + 2 * blurMargin).Within(0.001f), "scaled, rotated and blurred height");
            Assert.That(ReadPixel(selected, bitmap, redSide), Is.EqualTo(expectedRed).Within(0.008f),
                "the rotated red side mixes in linear RGB and keeps its alpha before fading");
            Assert.That(ReadPixel(selected, bitmap, justOutside)[3], Is.InRange(0.02f, alpha * 0.8f),
                "the Gaussian blur extends past the transformed edge");
            for (int i = 0; i < unselected.Length; i++)
            {
                EffectTarget target = targets[i + 1];
                Assert.That(target, Is.SameAs(unselected[i]), $"unselected target {i}");
                Assert.That(target.Bounds, Is.EqualTo(originalBounds[i]));
                Assert.That(ReadBits(target), Is.EqualTo(originalPixels[i]), $"unselected pixels {i}");
            }
        });
    }

    private static EffectTarget CreatePart(Rect bounds)
    {
        PixelRect pixels = PixelRect.FromRect(bounds, 1);
        using RenderTarget backing = CpuRenderTargets.Create(pixels.Width, pixels.Height);
        SKCanvas canvas = backing.Value.Canvas;
        canvas.Clear(new SKColorF(1, 0, 0, 0.8f));
        using var paint = new SKPaint { ColorF = new SKColorF(0, 1, 0, 0.8f), BlendMode = SKBlendMode.Src };
        canvas.DrawRect(pixels.Width / 2, 0, pixels.Width - pixels.Width / 2, pixels.Height, paint);
        canvas.Flush();
        return new EffectTarget(backing, bounds, EffectiveScale.At(1), pixels);
    }

    private static ushort[] ReadBits(EffectTarget target)
    {
        using Bitmap bitmap = target.RenderTarget!.Snapshot();
        return bitmap.GetPixelSpan<ushort>().ToArray();
    }

    private static float[] ReadPixel(EffectTarget target, Bitmap bitmap, Point position)
    {
        Rect raster = target.Placement.RasterBounds;
        int x = (int)MathF.Floor(position.X - raster.X);
        int y = (int)MathF.Floor(position.Y - raster.Y);
        // Outside the allocated raster is transparent, including when a regression omits the blur.
        if (x < 0 || x >= bitmap.Width || y < 0 || y >= bitmap.Height)
            return [0, 0, 0, 0];

        return bitmap.GetRow<ushort>(y).Slice(x * 4, 4).ToArray()
            .Select(bits => (float)BitConverter.UInt16BitsToHalf(bits)).ToArray();
    }
}
