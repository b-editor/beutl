using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
[NonParallelizable]
public sealed class GradientMapTests
{
    private const int Size = 8;

    // The pixel in the middle of a frame of one color, as blue, green, red and alpha in sRGB without premultiplying.
    private static byte[] Render(Color fill, FilterEffect? effect)
    {
        byte[] pixel = [];
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var shape = new RectShape { Width = { Value = Size }, Height = { Value = Size } };
            shape.Fill.Value = new SolidColorBrush(fill);
            shape.FilterEffect.Value = effect;
            using var resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                resource, new PixelSize(Size, Size), 1f, clearColor: Colors.Transparent);
            using Bitmap bytes = rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            pixel = bytes.GetRow(Size / 2).Slice(Size / 2 * 4, 4).ToArray();
        });
        return pixel;
    }

    private static Color Gray(byte level) => Color.FromRgb(level, level, level);

    private static GradientMap WithStops(params (Color Color, float Offset)[] stops)
    {
        var effect = new GradientMap();
        effect.GradientStops.Clear();
        foreach ((Color color, float offset) in stops)
            effect.GradientStops.Add(new GradientStop(color, offset));
        return effect;
    }

    [SetUp]
    public void SetUp() => VulkanTestEnvironment.EnsureAvailable();

    [TestCase(32)]
    [TestCase(64)]
    [TestCase(128)]
    [TestCase(192)]
    [TestCase(240)]
    public void Black_to_white_leaves_a_gray_picture_as_it_is(int level)
    {
        byte[] pixel = Render(Gray((byte)level), new GradientMap());

        Assert.That(pixel[1], Is.EqualTo(level).Within(2));
    }

    [Test]
    public void A_color_becomes_the_gray_of_its_lightness()
    {
        byte[] pixel = Render(Color.FromRgb(200, 120, 60), new GradientMap());

        Assert.Multiple(() =>
        {
            Assert.That(pixel[0], Is.EqualTo(pixel[2]).Within(1));
            Assert.That(pixel[1], Is.EqualTo(pixel[2]).Within(1));
            Assert.That(pixel[1], Is.InRange(120, 150));
        });
    }

    // Halfway up in sRGB is a fifth of the way in linear light, where a stop in the middle would hardly show.
    [Test]
    public void The_middle_of_the_gradient_falls_on_the_middle_tones()
    {
        GradientMap effect = WithStops((Colors.Black, 0), (Colors.Red, 0.5f), (Colors.White, 1));

        byte[] pixel = Render(Gray(128), effect);

        Assert.Multiple(() =>
        {
            Assert.That(pixel[2], Is.EqualTo(255).Within(3));
            Assert.That(pixel[1], Is.LessThan(8));
        });
    }

    [Test]
    public void The_interpolation_space_changes_how_the_stops_mix()
    {
        byte[] pixel = Render(Gray(128), new GradientMap { InterpolationSpace = { Value = GradientInterpolationSpace.LinearSrgb } });

        Assert.That(pixel[1], Is.EqualTo(188).Within(3));
    }

    [TestCase(40, 0, 50, 80)]
    [TestCase(200, 50, 100, 145)]
    [TestCase(64, 100, 0, 191)]
    [TestCase(64, 50, 50, 0)]
    [TestCase(200, 50, 50, 255)]
    public void The_input_range_stretches_over_the_gradient(int level, float black, float white, int expected)
    {
        byte[] pixel = Render(Gray((byte)level), new GradientMap { InputBlack = { Value = black }, InputWhite = { Value = white } });

        Assert.That(pixel[1], Is.EqualTo(expected).Within(5));
    }

    // Brightness carries the picture past white, where the gradient has already ended.
    [Test]
    public void What_is_lighter_than_white_takes_the_end_of_the_gradient()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new Brightness { Amount = { Value = 400 } });
        group.Children.Add(WithStops((Colors.Black, 0), (Colors.Red, 1)));

        byte[] pixel = Render(Gray(200), group);

        Assert.That(pixel, Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
    }

    [Test]
    public void The_pictures_alpha_stays_and_a_transparent_stop_takes_it_away()
    {
        byte[] translucent = Render(Color.FromArgb(128, 255, 255, 255), WithStops((Colors.Black, 0), (Colors.Red, 1)));
        byte[] hidden = Render(Colors.White, WithStops((Colors.Black, 0), (Color.FromArgb(0, 255, 255, 255), 1)));

        Assert.Multiple(() =>
        {
            Assert.That(translucent[3], Is.EqualTo(128).Within(1));
            Assert.That(translucent[2], Is.EqualTo(255).Within(1));
            Assert.That(translucent[1], Is.LessThan(4));
            Assert.That(hidden[3], Is.Zero);
        });
    }

    // Halfway in linear light between 0.216 in all channels and pure red.
    [Test]
    public void Half_strength_lies_between_the_picture_and_the_gradient()
    {
        GradientMap effect = WithStops((Colors.Red, 0), (Colors.Red, 1));
        effect.Strength.Value = 50;

        byte[] pixel = Render(Gray(128), effect);

        Assert.Multiple(() =>
        {
            Assert.That(pixel[2], Is.EqualTo(204).Within(3));
            Assert.That(pixel[1], Is.EqualTo(92).Within(3));
        });
    }

    [Test]
    public void Nothing_runs_without_stops_or_strength()
    {
        var bounds = new Rect(0, 0, Size, Size);
        foreach (GradientMap effect in new[] { WithStops(), new GradientMap { Strength = { Value = 0 } } })
        {
            using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
            using var context = new FilterEffectContext(bounds);

            context.ApplyTransactional(resource);

            Assert.That(context.GetOrderedItems(), Is.Empty);
        }
    }

    [Test]
    public void One_stop_paints_everything_its_color()
    {
        byte[] pixel = Render(Gray(128), WithStops((Colors.Red, 0.5f)));

        Assert.That(pixel, Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
    }

    [Test]
    public void The_stops_of_a_saved_effect_replace_the_default_ones()
    {
        GradientMap effect = WithStops((Colors.Red, 0), (Colors.Green, 0.5f), (Colors.Blue, 1));

        JsonObject json = CoreSerializer.SerializeToJsonObject(effect);
        var restored = (GradientMap)CoreSerializer.DeserializeFromJsonObject(json, typeof(GradientMap));

        Assert.That(restored.GradientStops.Select(stop => stop.Color.Value), Is.EqualTo(new[] { Colors.Red, Colors.Green, Colors.Blue }));
    }
}
