using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics;

// DrawRenderTarget and DrawSurface blit a surface at a point. While the canvas keeps the device axes at unit
// scale the blit copies pixels, even at a fractional offset or under a flip; under a scale or a rotation it
// must resample, like a scaled image draw, instead of dropping and duplicating source pixels.
[TestFixture]
public class ImmediateCanvasPointBlitSamplingTests
{
    private const int SourceSize = 8;
    private const int TargetSize = 40;

    private static readonly Point s_offset = new(1.5f, 2.25f);
    private static readonly SKSamplingOptions s_nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);
    private static readonly SKSamplingOptions s_linear = new(SKFilterMode.Linear, SKMipmapMode.None);

    public enum Transform
    {
        Upscale,
        Downscale,
        Rotation,
        FractionalTranslation,
        Flip,
    }

    [TestCase(Transform.Upscale)]
    [TestCase(Transform.Downscale)]
    [TestCase(Transform.Rotation)]
    public void DrawRenderTarget_ResamplesAwayFromUnitDeviceScale(Transform transform)
    {
        Matrix matrix = CreateMatrix(transform);
        using RenderTarget source = CreateCheckerboard();

        using Bitmap actual = Render(matrix, canvas => canvas.DrawRenderTarget(source, s_offset));
        using Bitmap resampled = RenderReference(matrix, source, s_linear);
        using Bitmap copied = RenderReference(matrix, source, s_nearest);

        Assert.Multiple(() =>
        {
            Assert.That(SamePixels(resampled, copied), Is.False, "The transform must tell resampling from copying.");
            Assert.That(SamePixels(actual, resampled), Is.True);
        });
    }

    [TestCase(Transform.FractionalTranslation)]
    [TestCase(Transform.Flip)]
    public void DrawRenderTarget_CopiesPixelsAtUnitDeviceScale(Transform transform)
    {
        Matrix matrix = CreateMatrix(transform);
        using RenderTarget source = CreateCheckerboard();

        using Bitmap actual = Render(matrix, canvas => canvas.DrawRenderTarget(source, s_offset));
        using Bitmap copied = RenderReference(matrix, source, s_nearest);

        Assert.That(SamePixels(actual, copied), Is.True);
    }

    [Test]
    public void DrawSurface_ResamplesUnderARotation()
    {
        Matrix matrix = CreateMatrix(Transform.Rotation);
        using RenderTarget source = CreateCheckerboard();

        using Bitmap actual = Render(matrix, canvas => canvas.DrawSurface(source.Value, s_offset));
        using Bitmap resampled = RenderReference(matrix, source, s_linear);

        Assert.That(SamePixels(actual, resampled), Is.True);
    }

    [Test]
    public void DrawSurface_CopiesPixelsAtAFractionalOffset()
    {
        Matrix matrix = CreateMatrix(Transform.FractionalTranslation);
        using RenderTarget source = CreateCheckerboard();

        using Bitmap actual = Render(matrix, canvas => canvas.DrawSurface(source.Value, s_offset));
        using Bitmap copied = RenderReference(matrix, source, s_nearest);

        Assert.That(SamePixels(actual, copied), Is.True);
    }

    private static Matrix CreateMatrix(Transform transform)
    {
        return transform switch
        {
            Transform.Upscale => Matrix.CreateScale(2.5f, 2.5f) * Matrix.CreateTranslation(3, 3),
            Transform.Downscale => Matrix.CreateScale(0.75f, 0.75f) * Matrix.CreateTranslation(5, 5),
            Transform.Rotation => Matrix.CreateTranslation(-4, -4)
                                  * Matrix.CreateRotation(MathF.PI / 7)
                                  * Matrix.CreateTranslation(20, 20),
            Transform.FractionalTranslation => Matrix.CreateTranslation(10.5f, 7.25f),
            Transform.Flip => Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(30, 10),
            _ => throw new ArgumentOutOfRangeException(nameof(transform)),
        };
    }

    private static RenderTarget CreateCheckerboard()
    {
        RenderTarget target = CreateTarget(SourceSize);
        SKCanvas canvas = target.Value.Canvas;
        canvas.Clear(SKColors.Black);
        using var white = new SKPaint { Color = SKColors.White };
        for (int y = 0; y < SourceSize; y++)
        {
            for (int x = y % 2; x < SourceSize; x += 2)
            {
                canvas.DrawRect(SKRect.Create(x, y, 1, 1), white);
            }
        }

        return target;
    }

    private static Bitmap Render(Matrix matrix, Action<ImmediateCanvas> draw)
    {
        using RenderTarget target = CreateTarget(TargetSize);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview);
        canvas.Clear();
        using (canvas.PushTransform(matrix))
        {
            draw(canvas);
        }

        return target.Snapshot();
    }

    // What ImmediateCanvas hands Skia for a point blit: the surface's snapshot drawn at the offset with an
    // antialiased paint, under the same matrix, with the given sampling.
    private static Bitmap RenderReference(Matrix matrix, RenderTarget source, SKSamplingOptions sampling)
    {
        using RenderTarget target = CreateTarget(TargetSize);
        SKCanvas canvas = target.Value.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Concat(matrix.ToSKMatrix());
        using (SKImage image = source.Value.Snapshot())
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.DrawImage(image, s_offset.X, s_offset.Y, sampling, paint);
        }

        canvas.Restore();
        return target.Snapshot();
    }

    private static RenderTarget CreateTarget(int size)
    {
        return RenderTarget.Create(size, size)
               ?? throw new InvalidOperationException("Could not create a raster render target.");
    }

    private static bool SamePixels(Bitmap actual, Bitmap expected)
    {
        return actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan());
    }
}
