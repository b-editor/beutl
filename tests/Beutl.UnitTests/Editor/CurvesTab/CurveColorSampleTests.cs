using Beutl.Editor.Components.CurvesTab.Services;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.UnitTests.Editor.CurvesTab;

[TestFixture]
public sealed class CurveColorSampleTests
{
    // The hues the Curves effect's rgb_to_hsv gives the primaries and secondaries, which its curves are drawn against.
    [TestCase(1, 0, 0, 0)]
    [TestCase(1, 1, 0, 1 / 6.0)]
    [TestCase(0, 1, 0, 2 / 6.0)]
    [TestCase(0, 1, 1, 3 / 6.0)]
    [TestCase(0, 0, 1, 4 / 6.0)]
    [TestCase(1, 0, 1, 5 / 6.0)]
    public void Hue_is_read_as_the_effect_reads_it(float red, float green, float blue, double hue)
    {
        CurveColorSample sample = CurveColorSample.FromLinear(red, green, blue);

        Assert.Multiple(() =>
        {
            Assert.That(sample.Hue, Is.EqualTo(hue).Within(1e-5));
            Assert.That(sample.Saturation, Is.EqualTo(1).Within(1e-5));
            Assert.That(sample.Value, Is.EqualTo(1).Within(1e-5));
        });
    }

    // The effect applies its curves to sRGB encoded values, so a linear 0.3185 is a value of 0.6.
    [Test]
    public void Values_are_encoded_as_srgb()
    {
        CurveColorSample sample = CurveColorSample.FromLinear(0.318547f, 0.318547f, 0.318547f);

        Assert.Multiple(() =>
        {
            Assert.That(sample.Value, Is.EqualTo(0.6).Within(1e-4));
            Assert.That(sample.Saturation, Is.EqualTo(0).Within(1e-6));
            Assert.That(sample.ToColor(), Is.EqualTo(Color.FromArgb(255, 153, 153, 153)));
        });
    }

    // Hue vs saturation and hue vs luminance read the hue after hue vs hue has turned it, also across red.
    [TestCase(0.1f, 0.6f, 0.2f)]
    [TestCase(0.95f, 0.6f, 0.05f)]
    [TestCase(0.05f, 0.4f, 0.95f)]
    public void The_hue_on_the_later_curves_is_turned_by_hue_vs_hue(float hue, float shift, float expected)
    {
        var sample = new CurveColorSample(1, 0, 0, hue, 1, 1);
        var hueVsHue = new CurveMap([new CurveControlPoint(0, shift), new CurveControlPoint(1, shift)]);

        Assert.Multiple(() =>
        {
            Assert.That(sample.HueOn(null), Is.EqualTo(hue).Within(1e-6));
            Assert.That(sample.HueOn(hueVsHue), Is.EqualTo(expected).Within(1e-5));
        });
    }

    // Pixels count by their coverage, as the renderer blends them, and a pixel with almost none is no color.
    [Test]
    public void The_pixels_around_a_point_count_by_their_coverage()
    {
        using var frame = new Bitmap(4, 4, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        Span<RgbaF16> row = frame.GetRow<RgbaF16>(1);
        // Half covered red beside fully covered red: still red, whatever the weights.
        row[1] = new RgbaF16((Half)0.5f, Half.Zero, Half.Zero, (Half)0.5f);
        row[2] = new RgbaF16(Half.One, Half.Zero, Half.Zero, Half.One);

        CurveColorSample? sample = CurveColorSample.Read(frame, 1, 1, radius: 1);
        CurveColorSample? empty = CurveColorSample.Read(frame, 3, 3, radius: 0);

        Assert.Multiple(() =>
        {
            Assert.That(sample, Is.Not.Null);
            Assert.That(sample!.Value.Hue, Is.EqualTo(0).Within(1e-4));
            Assert.That(sample.Value.Red, Is.EqualTo(1).Within(1e-3));
            Assert.That(empty, Is.Null);
        });
    }

    [Test]
    public void A_frame_in_another_format_is_read_as_the_renderer_makes_it()
    {
        using var frame = new Bitmap(2, 2, BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        frame.GetRow<Bgra8888>(0)[0] = new Bgra8888(0, 255, 0, 255);

        CurveColorSample? sample = CurveColorSample.Read(frame, 0, 0, radius: 0);

        Assert.That(sample?.Hue, Is.EqualTo(1 / 3.0).Within(1e-3));
    }
}
