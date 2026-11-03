using Beutl.Graphics.Effects;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class LumaKeyTests
{
    // In linear light, 128 in sRGB is about 22 % and 188 about 50 %.
    private static readonly SKColor s_dark = new(128, 128, 128);
    private static readonly SKColor s_bright = new(188, 188, 188);

    private static float AlphaOf(LumaKey key, SKColor color)
        => FilterEffectTestHarness.RunOnSolidTarget(key, color, new PixelSize(1, 1)).After[3];

    [Test]
    public void It_removes_what_is_darker_than_the_lower_limit()
    {
        var key = new LumaKey { Lower = { Value = 35 }, Softness = { Value = 0 } };

        Assert.Multiple(() =>
        {
            Assert.That(AlphaOf(key, s_dark), Is.Zero);
            Assert.That(AlphaOf(key, s_bright), Is.EqualTo(1f).Within(0.001f));
        });
    }

    [Test]
    public void It_removes_what_is_brighter_than_the_upper_limit()
    {
        var key = new LumaKey { Lower = { Value = 0 }, Upper = { Value = 35 }, Softness = { Value = 0 } };

        Assert.Multiple(() =>
        {
            Assert.That(AlphaOf(key, s_dark), Is.EqualTo(1f).Within(0.001f));
            Assert.That(AlphaOf(key, s_bright), Is.Zero);
        });
    }

    [Test]
    public void Invert_keeps_what_the_limits_would_remove()
    {
        var key = new LumaKey { Lower = { Value = 35 }, Softness = { Value = 0 }, Invert = { Value = true } };

        Assert.Multiple(() =>
        {
            Assert.That(AlphaOf(key, s_dark), Is.EqualTo(1f).Within(0.001f));
            Assert.That(AlphaOf(key, s_bright), Is.Zero);
        });
    }

    [Test]
    public void Softness_fades_below_the_lower_limit()
    {
        // 188 is 50 % in linear light, in the middle of a band from 40 % to 60 %.
        var key = new LumaKey { Lower = { Value = 60 }, Softness = { Value = 20 } };

        Assert.That(AlphaOf(key, s_bright), Is.EqualTo(0.5f).Within(0.05f));
    }

    [Test]
    public void What_it_keeps_keeps_its_color()
    {
        var key = new LumaKey { Lower = { Value = 60 }, Softness = { Value = 20 } };

        (float[] before, float[] after) = FilterEffectTestHarness.RunOnSolidTarget(key, s_bright, new PixelSize(1, 1));

        // Premultiplied, so the color divided by the alpha is what it was.
        Assert.That(after[0] / after[3], Is.EqualTo(before[0] / before[3]).Within(0.01f));
    }

    [Test]
    public void A_transparent_pixel_stays_transparent()
    {
        var key = new LumaKey { Lower = { Value = 0 }, Invert = { Value = true } };

        Assert.That(AlphaOf(key, SKColors.Transparent), Is.Zero);
    }

    [Test]
    public void Black_is_kept_when_the_lower_limit_is_zero()
    {
        var key = new LumaKey { Lower = { Value = 0 } };

        Assert.That(AlphaOf(key, SKColors.Black), Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void Brightness_comes_from_the_color_not_from_how_transparent_it_is()
    {
        var key = new LumaKey { Lower = { Value = 35 }, Softness = { Value = 0 } };

        Assert.That(AlphaOf(key, s_bright.WithAlpha(128)), Is.EqualTo(128 / 255f).Within(0.01f));
    }
}
