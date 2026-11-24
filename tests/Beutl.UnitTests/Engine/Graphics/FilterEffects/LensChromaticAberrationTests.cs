using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
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
    private static readonly PixelSize s_frame = new(200, 200);

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
            RepeatEdgePixels = false,
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
        using var resource = new LensChromaticAberration.Resource
        {
            Amount = amount,
            Center = RelativePoint.Center,
            RepeatEdgePixels = false,
        };
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);

        Assert.That(context.Bounds.Width, Is.EqualTo(110f).Within(0.001f));
    }

    [Test]
    public void DefaultSettingsKeepTheInputBounds()
    {
        using FilterEffect.Resource resource = new LensChromaticAberration().ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.GetOrderedItems(), Is.Not.Empty);
            Assert.That(context.Bounds, Is.EqualTo(s_bounds));
        }
    }

    [TestCase(10f, 0.25f, 0.75f)]
    [TestCase(-10f, 1.5f, -0.5f)]
    public void RepeatedEdgePixelsKeepTheInputBounds(float amount, float centerX, float centerY)
    {
        using var resource = new LensChromaticAberration.Resource
        {
            Amount = amount,
            Center = new RelativePoint(centerX, centerY, RelativeUnit.Relative),
            RepeatEdgePixels = true,
        };
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(resource);

        Assert.That(context.Bounds, Is.EqualTo(s_bounds));
    }

    [Test]
    public void RepeatsTheOutermostPixelsPastTheEdges()
    {
        // Yellow between two white columns. The blue that the outward channel reads past either edge is that column's.
        using Bitmap bitmap = Render(10f, repeatEdgePixels: true, DrawYellowBetweenWhiteColumns, out Rect raster);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raster, Is.EqualTo(new Rect(0, 0, s_patternSize.Width, s_patternSize.Height)));
            foreach (int x in (int[])[0, 4, 96, 100])
                Assert.That(PixelAt(bitmap, x, 10, raster), Is.EqualTo(new Rgba(1, 1, 1, 1)), $"x = {x}");
            Assert.That(PixelAt(bitmap, 50, 10, raster), Is.EqualTo(new Rgba(1, 1, 0, 1)));
        }
    }

    [Test]
    public void DefaultSettingsKeepAnOpaquePictureInItsBoundsWithItsEdgeColors()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            const int surface = 1000;
            var content = new Rect(100, 100, 800, 800);
            using var fill = (Brush.Resource)new SolidColorBrush(Color.FromRgb(220, 170, 120))
                .ToResource(CompositionContext.Default);
            using Bitmap bitmap = FilteredShapeHarness.Render(content, new LensChromaticAberration(), fullBleed: true,
                surface, new Rect(0, 0, surface, surface), fill);
            Rgba color = PixelAt(bitmap, 500, 500);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(MathF.Min(color.Red, MathF.Min(color.Green, color.Blue)), Is.GreaterThan(0.1f));
                // At 1 %, the channel that reads outward found nothing within four pixels of each edge.
                foreach (int inset in (int[])[0, 1, 2, 3, 6])
                {
                    AssertColor(PixelAt(bitmap, 100 + inset, 500), color, $"{inset} px inside the left edge");
                    AssertColor(PixelAt(bitmap, 899 - inset, 500), color, $"{inset} px inside the right edge");
                    AssertColor(PixelAt(bitmap, 500, 100 + inset), color, $"{inset} px inside the top edge");
                    AssertColor(PixelAt(bitmap, 500, 899 - inset), color, $"{inset} px inside the bottom edge");
                }

                // The channel that reads inward used to draw a fringe there.
                foreach (int gap in (int[])[1, 3])
                {
                    Assert.That(PixelAt(bitmap, 100 - gap, 500), Is.EqualTo(default(Rgba)), $"{gap} px left of the picture");
                    Assert.That(PixelAt(bitmap, 899 + gap, 500), Is.EqualTo(default(Rgba)), $"{gap} px right of the picture");
                    Assert.That(PixelAt(bitmap, 500, 100 - gap), Is.EqualTo(default(Rgba)), $"{gap} px above the picture");
                    Assert.That(PixelAt(bitmap, 500, 899 + gap), Is.EqualTo(default(Rgba)), $"{gap} px below the picture");
                }
            }
        });
    }

    [Test]
    public void RepeatedEdgesKeepThePartialCoverageOfFractionalBounds()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            const int surface = 1000;
            // The left and right edges cover three quarters of their pixels.
            var content = new Rect(100.25f, 100, 799.5f, 800);
            using var fill = (Brush.Resource)new SolidColorBrush(Color.FromRgb(220, 170, 120))
                .ToResource(CompositionContext.Default);
            using Bitmap bitmap = FilteredShapeHarness.Render(content, new LensChromaticAberration(), fullBleed: true,
                surface, new Rect(0, 0, surface, surface), fill);
            Rgba color = PixelAt(bitmap, 500, 500);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(PixelAt(bitmap, 100, 500).Alpha, Is.EqualTo(0.75f).Within(0.02f), "the left edge pixel");
                Assert.That(PixelAt(bitmap, 899, 500).Alpha, Is.EqualTo(0.75f).Within(0.02f), "the right edge pixel");
                AssertColor(PixelAt(bitmap, 101, 500), color, "the first whole pixel");
                AssertColor(PixelAt(bitmap, 898, 500), color, "the last whole pixel");
            }
        });
    }

    [TestCase(1.5f)]
    [TestCase(2f)]
    public void RepeatedEdgesKeepTheirColorsAtOtherDensities(float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // The channel that reads outward reaches past every edge of the picture, which spans (20, 40) to (180, 160).
            using Drawable.Resource resource = Picture(new LensChromaticAberration
            {
                Amount = { Value = 10f },
                Center = { Value = new RelativePoint(0.85f, 0.4f, RelativeUnit.Relative) },
            }, new SolidColorBrush(Color.FromRgb(220, 170, 120)));
            using Bitmap bitmap = GoldenImageHarness.RenderAtScale(resource, s_frame, scale, clearColor: Colors.Transparent);
            int left = (int)(20 * scale), right = (int)(180 * scale) - 1, top = (int)(40 * scale), bottom = (int)(160 * scale) - 1;
            int x = (int)(100 * scale), y = (int)(100 * scale);
            Rgba color = PixelAt(bitmap, x, y);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(MathF.Min(color.Red, MathF.Min(color.Green, color.Blue)), Is.GreaterThan(0.1f));
                foreach (int inset in (int[])[0, 1, 2, 4])
                {
                    AssertColor(PixelAt(bitmap, left + inset, y), color, $"{inset} px inside the left edge");
                    AssertColor(PixelAt(bitmap, right - inset, y), color, $"{inset} px inside the right edge");
                    AssertColor(PixelAt(bitmap, x, top + inset), color, $"{inset} px inside the top edge");
                    AssertColor(PixelAt(bitmap, x, bottom - inset), color, $"{inset} px inside the bottom edge");
                }

                Assert.That(PixelAt(bitmap, left - 1, y), Is.EqualTo(default(Rgba)), "left of the picture");
                Assert.That(PixelAt(bitmap, right + 1, y), Is.EqualTo(default(Rgba)), "right of the picture");
                Assert.That(PixelAt(bitmap, x, top - 1), Is.EqualTo(default(Rgba)), "above the picture");
                Assert.That(PixelAt(bitmap, x, bottom + 1), Is.EqualTo(default(Rgba)), "below the picture");
            }
        });
    }

    [TestCase(false, 1.5f)]
    [TestCase(false, 2f)]
    [TestCase(true, 1.5f)]
    [TestCase(true, 2f)]
    public void PartialRequestMatchesTheWhole(bool repeatEdgePixels, float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // Six units inside the picture's left edge, where the blue channel reads past the edge.
            var region = new Rect(26, 80, 8, 24);
            PixelRect deviceRegion = PixelRect.FromRect(region, scale);
            using Drawable.Resource resource = Picture(new LensChromaticAberration
            {
                Amount = { Value = 10f },
                Center = { Value = new RelativePoint(0.85f, 0.4f, RelativeUnit.Relative) },
                RepeatEdgePixels = { Value = repeatEdgePixels },
            }, Stripes());
            using Drawable.Resource controlResource = Picture(new FilterEffectGroup(), Stripes());
            using Bitmap full = GoldenImageHarness.RenderAtScale(resource, s_frame, scale, clearColor: Colors.Transparent);
            using Bitmap partial = GoldenImageHarness.RenderAtScale(
                resource, s_frame, scale, requestedRegion: region, clearColor: Colors.Transparent);
            using Bitmap control = GoldenImageHarness.RenderAtScale(controlResource, s_frame, scale, clearColor: Colors.Transparent);
            using Bitmap expected = full.ExtractSubset(deviceRegion);
            using Bitmap actual = partial.ExtractSubset(deviceRegion);
            using Bitmap unfiltered = control.ExtractSubset(deviceRegion);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ImageMetrics.MaximumAbsoluteErrorPerChannel(expected, actual).Maximum, Is.LessThanOrEqualTo(0.005));
                Assert.That(ImageMetrics.MeanAbsoluteError(expected, unfiltered), Is.GreaterThan(0.02),
                    "the requested region must show the effect");
            }
        });
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
                RepeatEdgePixels = { Value = false },
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

    private static void AssertColor(Rgba actual, Rgba expected, string message)
    {
        float difference = MathF.Max(
            MathF.Max(MathF.Abs(actual.Red - expected.Red), MathF.Abs(actual.Green - expected.Green)),
            MathF.Max(MathF.Abs(actual.Blue - expected.Blue), MathF.Abs(actual.Alpha - expected.Alpha)));
        Assert.That(difference, Is.LessThanOrEqualTo(0.002f), $"{message}: {actual} instead of {expected}");
    }

    // A 160 x 120 picture in the middle of the frame.
    private static Drawable.Resource Picture(FilterEffect effect, Brush fill)
    {
        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.Width.Value = 160;
        shape.Height.Value = 120;
        shape.Fill.Value = fill;
        shape.FilterEffect.Value = effect;
        return shape.ToResource(CompositionContext.Default);
    }

    private static LinearGradientBrush Stripes()
    {
        var stripes = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0, 0, RelativeUnit.Absolute) },
            EndPoint = { Value = new RelativePoint(12, 0, RelativeUnit.Absolute) },
            SpreadMethod = { Value = GradientSpreadMethod.Repeat },
        };
        stripes.GradientStops.Add(new GradientStop(Colors.White, 0));
        stripes.GradientStops.Add(new GradientStop(Colors.White, 0.5f));
        stripes.GradientStops.Add(new GradientStop(Colors.Gray, 0.5f));
        stripes.GradientStops.Add(new GradientStop(Colors.Gray, 1));
        return stripes;
    }

    // The fringe tests keep the transparent edges they were written for.
    private static Bitmap RenderPattern(float amount, SKColor color, out Rect rasterBounds)
        => Render(amount, repeatEdgePixels: false, canvas => DrawPattern(canvas, color), out rasterBounds);

    private static Bitmap Render(float amount, bool repeatEdgePixels, Action<SKCanvas> draw, out Rect rasterBounds)
    {
        VulkanTestEnvironment.EnsureAvailable();
        (Bitmap bitmap, Rect raster) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget backing = RenderTarget.Create(s_patternSize.Width, s_patternSize.Height)!;
            draw(backing.Value.Canvas);
            backing.Value.Canvas.Flush();
            var bounds = new Rect(0, 0, s_patternSize.Width, s_patternSize.Height);
            using var targets = new EffectTargets
            {
                new EffectTarget(backing, bounds, EffectiveScale.At(1), PixelRect.FromRect(bounds, 1)),
            };
            var effect = new LensChromaticAberration
            {
                Amount = { Value = amount },
                RepeatEdgePixels = { Value = repeatEdgePixels },
            };
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

    private static void DrawYellowBetweenWhiteColumns(SKCanvas canvas)
    {
        canvas.Clear(SKColors.Yellow);
        using var paint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(0, 0, 1, s_patternSize.Height, paint);
        canvas.DrawRect(s_patternSize.Width - 1, 0, 1, s_patternSize.Height, paint);
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
