using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
[NonParallelizable]
public sealed class BokehTests
{
    private const int Size = 160;
    private const int DotSize = 8;

    // BGRA bytes in sRGB without premultiplying, of a white dot in the middle of a black frame.
    private static byte[] RenderDot(FilterEffect? effect, float scale = 1f, Rect? region = null)
    {
        byte[] pixels = [];
        int size = (int)(Size * scale);
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var dot = new RectShape { Width = { Value = DotSize }, Height = { Value = DotSize } };
            dot.Fill.Value = new SolidColorBrush(Colors.White);
            dot.FilterEffect.Value = effect;
            using var resource = (Drawable.Resource)dot.ToResource(CompositionContext.Default);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(resource, new PixelSize(Size, Size), scale, region);
            using Bitmap bytes = rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                bytes.GetRow(y).CopyTo(pixels.AsSpan(y * size * 4));
        });
        return pixels;
    }

    private static int GreenAt(byte[] pixels, int x, int y, int size = Size) => pixels[(y * size + x) * 4 + 1];

    // The distance from the middle at which the disc falls under half of what it is around the middle.
    private static float Reach(byte[] pixels, float directionX, float directionY, int size = Size)
    {
        int middle = size / 2;
        float length = MathF.Sqrt(directionX * directionX + directionY * directionY);
        int level = GreenAt(pixels, middle, middle, size);
        for (int distance = 1; distance < middle; distance++)
        {
            int x = middle + (int)MathF.Round(directionX / length * distance);
            int y = middle + (int)MathF.Round(directionY / length * distance);
            if (GreenAt(pixels, x, y, size) < level / 2)
                return distance;
        }

        return middle;
    }

    [SetUp]
    public void SetUp() => VulkanTestEnvironment.EnsureAvailable();

    [TestCase(16f, BokehQuality.Low)]
    [TestCase(16f, BokehQuality.Medium)]
    [TestCase(16f, BokehQuality.High)]
    [TestCase(48f, BokehQuality.Medium)]
    public void A_point_spreads_into_an_even_disc_of_the_radius(float radius, BokehQuality quality)
    {
        byte[] pixels = RenderDot(new Bokeh { Radius = { Value = radius }, Quality = { Value = quality } });

        Assert.Multiple(() =>
        {
            foreach ((float x, float y) in new[] { (1f, 0f), (0f, 1f), (-1f, 0f), (0f, -1f), (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) })
                Assert.That(Reach(pixels, x, y), Is.EqualTo(radius).Within(radius * 0.2f + 2), $"towards {x}, {y}");

            // No echoes of the dot inside the disc.
            int level = GreenAt(pixels, Size / 2, Size / 2);
            int inner = (int)(radius * 0.6f);
            for (int y = -inner; y <= inner; y += 3)
            for (int x = -inner; x <= inner; x += 3)
            {
                if (x * x + y * y <= inner * inner)
                    Assert.That(GreenAt(pixels, Size / 2 + x, Size / 2 + y), Is.EqualTo(level).Within(level / 3), $"at {x}, {y}");
            }
        });
    }

    [Test]
    public void A_square_aperture_reaches_furthest_towards_its_corners_and_the_rotation_turns_them()
    {
        const float Radius = 40;
        byte[] upright = RenderDot(new Bokeh { Radius = { Value = Radius }, Shape = { Value = BokehShape.Polygon }, Sides = { Value = 4 } });
        byte[] turned = RenderDot(new Bokeh
        {
            Radius = { Value = Radius },
            Shape = { Value = BokehShape.Polygon },
            Sides = { Value = 4 },
            Rotation = { Value = 45 },
        });

        Assert.Multiple(() =>
        {
            // With a corner up, the corners are on the axes.
            Assert.That(Reach(upright, 0, -1) / Reach(upright, 1, -1), Is.EqualTo(MathF.Sqrt(2)).Within(0.3f));
            Assert.That(Reach(upright, 1, 0) / Reach(upright, 1, 1), Is.EqualTo(MathF.Sqrt(2)).Within(0.3f));
            Assert.That(Reach(turned, 1, -1) / Reach(turned, 0, -1), Is.EqualTo(MathF.Sqrt(2)).Within(0.3f));
        });
    }

    [Test]
    public void The_radius_follows_the_output_scale()
    {
        byte[] pixels = RenderDot(new Bokeh { Radius = { Value = 16 } }, scale: 2);

        Assert.That(Reach(pixels, 1, 0, Size * 2), Is.EqualTo(32).Within(8));
    }

    [Test]
    public void The_same_settings_draw_the_same_picture()
    {
        byte[] first = RenderDot(new Bokeh { Radius = { Value = 24 }, Shape = { Value = BokehShape.Polygon } });
        byte[] second = RenderDot(new Bokeh { Radius = { Value = 24 }, Shape = { Value = BokehShape.Polygon } });

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void The_highlight_makes_the_bright_parts_brighter_before_they_spread()
    {
        byte[] plain = RenderDot(new Bokeh { Radius = { Value = 24 } });
        byte[] boosted = RenderDot(new Bokeh { Radius = { Value = 24 }, Highlight = { Value = 200 } });

        static float Linear(int level) => MathF.Pow(level / 255f, 2.2f);
        float gain = Linear(GreenAt(boosted, Size / 2, Size / 2)) / Linear(GreenAt(plain, Size / 2, Size / 2));

        Assert.That(gain, Is.EqualTo(3).Within(0.5f));
    }

    [Test]
    public void The_highlight_leaves_what_is_not_bright_alone()
    {
        byte[] pixel = [];
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var shape = new RectShape { Width = { Value = Size }, Height = { Value = Size } };
            shape.Fill.Value = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            shape.FilterEffect.Value = new Bokeh { Radius = { Value = 8 }, Highlight = { Value = 500 } };
            using var resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(resource, new PixelSize(Size, Size), 1f);
            using Bitmap bytes = rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            pixel = bytes.GetRow(Size / 2).Slice(Size / 2 * 4, 4).ToArray();
        });

        Assert.That(pixel[1], Is.EqualTo(100).Within(1));
    }

    [Test]
    public void The_spread_goes_past_the_picture_on_nothing_without_changing_its_color()
    {
        byte[] row = [];
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var dot = new RectShape { Width = { Value = DotSize * 2 }, Height = { Value = DotSize * 2 } };
            dot.Fill.Value = new SolidColorBrush(Color.FromRgb(200, 100, 50));
            dot.FilterEffect.Value = new Bokeh { Radius = { Value = 16 } };
            using var resource = (Drawable.Resource)dot.ToResource(CompositionContext.Default);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                resource, new PixelSize(Size, Size), 1f, clearColor: Colors.Transparent);
            using Bitmap bytes = rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            row = bytes.GetRow(Size / 2).ToArray();
        });

        int inside = (Size / 2 + 12) * 4;
        int outside = (Size / 2 + 30) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(row[inside + 3], Is.GreaterThan(20));
            Assert.That(row[inside + 2], Is.EqualTo(200).Within(4));
            Assert.That(row[inside + 1], Is.EqualTo(100).Within(4));
            Assert.That(row[inside], Is.EqualTo(50).Within(4));
            Assert.That(row[outside + 3], Is.Zero);
        });
    }

    // Away from the dot the disc is all that is there, and it comes from outside the part.
    [TestCase(24f)]
    [TestCase(48f)]
    public void Drawing_part_of_the_frame_reads_as_far_around_it_as_the_shape_reaches(float radius)
    {
        var effect = new Bokeh { Radius = { Value = radius } };
        byte[] whole = RenderDot(effect);
        int start = Size / 2 + (int)(radius * 0.6f);

        byte[] part = RenderDot(effect, region: new Rect(start, Size / 2 - 2, 4, 4));

        Assert.Multiple(() =>
        {
            Assert.That(GreenAt(whole, start, Size / 2), Is.GreaterThan(10));
            Assert.That(GreenAt(part, start, Size / 2), Is.EqualTo(GreenAt(whole, start, Size / 2)).Within(2));
        });
    }

    // The radius and one distance between the five rings of the default quality, which is as far as the soft edge reaches.
    [Test]
    public void The_bounds_grow_as_far_as_the_shape_can_reach()
    {
        var bounds = new Rect(10, 20, 100, 60);
        using FilterEffect.Resource resource = new Bokeh { Radius = { Value = 10 } }.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);

        context.ApplyTransactional(resource);

        Assert.That(context.Bounds, Is.EqualTo(bounds.Inflate(12)));
    }

    [Test]
    public void Nothing_runs_without_a_radius()
    {
        using FilterEffect.Resource resource = new Bokeh { Radius = { Value = 0 } }.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, 10, 10));

        context.ApplyTransactional(resource);

        Assert.That(context.GetOrderedItems(), Is.Empty);
    }
}
