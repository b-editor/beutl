using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// Guards the "rasterize once at the final density" contract: content whose buffer already lands on
/// exact device pixels must be copied, never resampled, and a genuine resample must stay inside the
/// range of the samples it interpolated.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class LosslessCompositeCoverageTests
{
    private static readonly PixelSize s_frame = new(200, 140);

    [TestCase(1f)]
    [TestCase(2f)]
    public void EffectFreeCurvedGeometry_MatchesDirectRasterization(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource resource = CreateEllipse(effect: null);
            using Bitmap expected = RenderDirect(resource, density);
            using Bitmap actual = RenderThroughPipeline(resource, density);

            AssertByteIdentical(expected, actual, $"effect-free ellipse at density {density}");
        });
    }

    [TestCase(1f)]
    [TestCase(2f)]
    public void IdentityColorEffect_PreservesEdgeCoverage(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var identity = new Brightness();
            identity.Amount.CurrentValue = 100;
            using Drawable.Resource plain = CreateRectangle(effect: null);
            using Drawable.Resource filtered = CreateRectangle(identity);
            using Bitmap expected = RenderThroughPipeline(plain, density);
            using Bitmap actual = RenderThroughPipeline(filtered, density);

            AssertByteIdentical(expected, actual, $"identity Brightness at density {density}");
        });
    }

    [Test]
    public void ScaledComposite_StaysInsideTheSourceRange()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create(16, 16)
                                        ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
            using (var sourceCanvas = new ImmediateCanvas(source, 1f, logicalSize: new Size(16, 16)))
            {
                sourceCanvas.Clear();
                using var dark = new SKPaint { IsAntialias = false, Color = new SKColor(64, 64, 64) };
                using var bright = new SKPaint { IsAntialias = false, Color = new SKColor(192, 192, 192) };
                sourceCanvas.Canvas.DrawRect(SKRect.Create(0, 0, 16, 8), dark);
                sourceCanvas.Canvas.DrawRect(SKRect.Create(0, 8, 16, 8), bright);
            }

            using RenderTarget destination = RenderTarget.Create(64, 64)
                                             ?? throw new InvalidOperationException(
                                                 "RenderTarget.Create returned null.");
            using (var canvas = new ImmediateCanvas(destination, 1f, logicalSize: new Size(64, 64)))
            {
                canvas.Clear();
                canvas.DrawRenderTargetScaled(source, new Rect(4, 4, 40, 40));
            }

            using Bitmap result = destination.Snapshot();
            double darkPlateau = ReadRed(result, 22, 10);
            double brightPlateau = ReadRed(result, 22, 38);
            double minimum = double.PositiveInfinity;
            double maximum = double.NegativeInfinity;
            for (int y = 6; y < 42; y++)
            {
                for (int x = 6; x < 42; x++)
                {
                    double value = ReadRed(result, x, y);
                    minimum = Math.Min(minimum, value);
                    maximum = Math.Max(maximum, value);
                }
            }

            // One RgbaF16 step near the bright plateau is ~4.9e-4, so only a real kernel lobe clears this.
            const double halfFloatTolerance = 1e-3;
            TestContext.WriteLine(
                $"plateaus [{darkPlateau:F6}, {brightPlateau:F6}], resampled [{minimum:F6}, {maximum:F6}]");
            Assert.Multiple(() =>
            {
                Assert.That(maximum, Is.LessThanOrEqualTo(brightPlateau + halfFloatTolerance),
                    "The resample kernel overshot the brightest sample it interpolated.");
                Assert.That(minimum, Is.GreaterThanOrEqualTo(darkPlateau - halfFloatTolerance),
                    "The resample kernel undershot the darkest sample it interpolated.");
            });
        });
    }

    private static double ReadRed(Bitmap bitmap, int x, int y)
        => (double)BitConverter.UInt16BitsToHalf(bitmap.GetPixelSpan<ushort>()[((y * bitmap.Width) + x) * 4]);

    private static void AssertByteIdentical(Bitmap expected, Bitmap actual, string scenario)
    {
        int differing = 0;
        double maximum = 0;
        ReadOnlySpan<ushort> a = expected.GetPixelSpan<ushort>();
        ReadOnlySpan<ushort> b = actual.GetPixelSpan<ushort>();
        for (int index = 0; index < a.Length; index++)
        {
            double left = (double)BitConverter.UInt16BitsToHalf(a[index]);
            double right = (double)BitConverter.UInt16BitsToHalf(b[index]);
            if (a[index] != b[index])
                differing++;
            maximum = Math.Max(maximum, Math.Abs(left - right));
        }

        Assert.Multiple(() =>
        {
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
            Assert.That(differing, Is.Zero,
                $"{scenario}: {differing} channels differ, maximum delta {maximum:F6}.");
        });
    }

    private static Drawable.Resource CreateEllipse(FilterEffect? effect)
    {
        var shape = new EllipseShape();
        shape.Width.CurrentValue = 120;
        shape.Height.CurrentValue = 80;
        return Configure(shape, effect);
    }

    private static Drawable.Resource CreateRectangle(FilterEffect? effect)
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = 120;
        shape.Height.CurrentValue = 80;
        return Configure(shape, effect);
    }

    private static Drawable.Resource Configure(Shape shape, FilterEffect? effect)
    {
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Fill.CurrentValue = Brushes.White;
        if (effect is not null)
            shape.FilterEffect.CurrentValue = effect;
        return shape.ToResource(CompositionContext.Default);
    }

    private static Bitmap RenderDirect(Drawable.Resource resource, float density)
    {
        var shape = (Shape)resource.GetOriginal();
        var shapeResource = (Shape.Resource)resource;
        Size frameSize = s_frame.ToSize(1);
        Size shapeSize = shape.MeasureInternal(frameSize, resource);
        Matrix transform = shape.GetTransformMatrix(frameSize, shapeSize, resource);
        Geometry.Resource geometry = shapeResource.GetGeometry()
                                     ?? throw new InvalidOperationException("The shape produced no geometry.");

        using RenderTarget target = CreateFrameTarget(density);
        using var canvas = new ImmediateCanvas(target, density, logicalSize: frameSize);
        canvas.Clear();
        using (canvas.PushTransform(transform))
        {
            canvas.DrawGeometry(geometry, shapeResource.Fill, shapeResource.Pen);
        }

        return target.Snapshot();
    }

    private static Bitmap RenderThroughPipeline(Drawable.Resource resource, float density)
    {
        using var node = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(node, s_frame.ToSize(1), density))
        {
            resource.GetOriginal().Render(context, resource);
        }

        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = new Rect(default, s_frame.ToSize(1)),
                OutputScale = density,
                MaxWorkingScale = density,
                UseRenderCache = false,
                RenderPurpose = RenderRequestPurpose.Frame,
            });

        using RenderTarget target = CreateFrameTarget(density);
        using var canvas = new ImmediateCanvas(target, density, logicalSize: s_frame.ToSize(1));
        canvas.Clear();
        renderer.Render(canvas);
        return target.Snapshot();
    }

    private static RenderTarget CreateFrameTarget(float density)
        => RenderTarget.Create(
               (int)MathF.Ceiling(s_frame.Width * density),
               (int)MathF.Ceiling(s_frame.Height * density))
           ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
}
