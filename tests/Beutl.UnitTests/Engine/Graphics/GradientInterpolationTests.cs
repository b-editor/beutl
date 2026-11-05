using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public sealed class GradientInterpolationTests
{
    private const int Size = 256;

    // Skia mixes in the space of the surface, so the surface is linear like the one the engine composites on. The middle
    // of black and white is 0.5 in linear light, 128 / 255 as stored in sRGB, and lightness 0.5 in OKLab.
    private static float Expected(GradientInterpolationSpace space) => space switch
    {
        GradientInterpolationSpace.Srgb => 0.2158f,
        GradientInterpolationSpace.Oklab => 0.125f,
        _ => 0.5f,
    };

    [Test]
    public void A_surface_that_mixes_in_srgb_gets_stops_for_linear_srgb_as_well()
    {
        SKColor[] colors = [SKColors.Black, SKColors.White];
        float[] offsets = [0f, 1f];

        (SKColor[] rendered, _) = GradientStopInterpolation.Expand(
            colors, offsets, GradientInterpolationSpace.LinearSrgb, premultiplied: false);
        (SKColor[] shown, float[] shownOffsets) = GradientStopInterpolation.ExpandForSrgbSurface(
            colors, offsets, GradientInterpolationSpace.LinearSrgb, premultiplied: false);

        Assert.That(rendered, Is.SameAs(colors), "Skia already mixes in linear sRGB on the render surface.");
        Assert.That(shown, Has.Length.GreaterThan(colors.Length));
        Assert.That(shown[Array.IndexOf(shownOffsets, 0.5f)].Red, Is.EqualTo(188));
    }

    [TestCase(GradientInterpolationSpace.LinearSrgb)]
    [TestCase(GradientInterpolationSpace.Srgb)]
    [TestCase(GradientInterpolationSpace.Oklab)]
    public void A_linear_gradient_mixes_in_its_space(GradientInterpolationSpace space)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
        };

        Assert.That(ReadMiddle(brush, space, Size / 2, Size / 2), Is.EqualTo(Expected(space)).Within(0.01f));
    }

    [TestCase(GradientInterpolationSpace.LinearSrgb)]
    [TestCase(GradientInterpolationSpace.Srgb)]
    [TestCase(GradientInterpolationSpace.Oklab)]
    public void A_radial_gradient_mixes_in_its_space(GradientInterpolationSpace space)
    {
        var brush = new RadialGradientBrush
        {
            Center = { Value = new RelativePoint(0.5f, 0.5f, RelativeUnit.Relative) },
            GradientOrigin = { Value = new RelativePoint(0.5f, 0.5f, RelativeUnit.Relative) },
            Radius = { Value = 50 },
        };

        Assert.That(ReadMiddle(brush, space, Size * 3 / 4, Size / 2), Is.EqualTo(Expected(space)).Within(0.01f));
    }

    // Halfway from the origin at a quarter of the width to the rim at the right edge.
    [TestCase(GradientInterpolationSpace.LinearSrgb)]
    [TestCase(GradientInterpolationSpace.Srgb)]
    [TestCase(GradientInterpolationSpace.Oklab)]
    public void A_radial_gradient_off_its_center_mixes_in_its_space(GradientInterpolationSpace space)
    {
        var brush = new RadialGradientBrush
        {
            Center = { Value = new RelativePoint(0.5f, 0.5f, RelativeUnit.Relative) },
            GradientOrigin = { Value = new RelativePoint(0.25f, 0.5f, RelativeUnit.Relative) },
            Radius = { Value = 50 },
        };

        Assert.That(ReadMiddle(brush, space, Size * 5 / 8, Size / 2), Is.EqualTo(Expected(space)).Within(0.01f));
    }

    [Test]
    public void A_radial_gradient_off_its_center_keeps_the_order_of_many_stops()
    {
        var brush = new RadialGradientBrush
        {
            Center = { Value = new RelativePoint(0.5f, 0.5f, RelativeUnit.Relative) },
            GradientOrigin = { Value = new RelativePoint(0.25f, 0.5f, RelativeUnit.Relative) },
            Radius = { Value = 50 },
        };
        brush.GradientStops.Add(new GradientStop(Colors.Black, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, 0.25f));
        brush.GradientStops.Add(new GradientStop(Colors.White, 0.75f));
        brush.GradientStops.Add(new GradientStop(Colors.White, 1));
        using Brush.Resource resource = brush.ToResource(CompositionContext.Default);
        var constructor = new BrushConstructor(
            new Rect(0, 0, Size, Size), resource, BlendMode.Src, RenderIntent.Preview, drawableBrushMaterializer: null);
        using SKShader? shader = constructor.CreateShader();
        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        using var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.RgbaF16, SKAlphaType.Premul, colorSpace));
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src })
        {
            canvas.DrawRect(SKRect.Create(Size, Size), paint);
        }

        using SKPixmap pixels = bitmap.PeekPixels();
        ReadOnlySpan<Half> values = MemoryMarshal.Cast<byte, Half>(pixels.GetPixelSpan());
        float black = (float)values[(Size / 2 * Size + Size / 4 + Size / 16) * 4];
        float middle = (float)values[(Size / 2 * Size + Size * 5 / 8) * 4];
        float white = (float)values[(Size / 2 * Size + Size * 15 / 16) * 4];
        Assert.Multiple(() =>
        {
            Assert.That(black, Is.EqualTo(0).Within(0.01f), "near the origin, in the black part");
            Assert.That(middle, Is.EqualTo(0.5f).Within(0.02f), "halfway");
            Assert.That(white, Is.EqualTo(1).Within(0.01f), "near the rim, in the white part");
        });
    }

    // A conic gradient starts straight above its center and goes around, so its middle is straight below.
    [TestCase(GradientInterpolationSpace.LinearSrgb)]
    [TestCase(GradientInterpolationSpace.Srgb)]
    [TestCase(GradientInterpolationSpace.Oklab)]
    public void A_conic_gradient_mixes_in_its_space(GradientInterpolationSpace space)
    {
        var brush = new ConicGradientBrush { Center = { Value = new RelativePoint(0.5f, 0.5f, RelativeUnit.Relative) } };

        Assert.That(ReadMiddle(brush, space, Size / 2, Size - 8), Is.EqualTo(Expected(space)).Within(0.01f));
    }

    [Test]
    public void The_default_leaves_the_stops_to_Skia()
    {
        SKColor[] colors = [SKColors.Black, SKColors.White];
        float[] offsets = [0, 1];

        (SKColor[] expandedColors, float[] expandedOffsets) = GradientStopInterpolation.Expand(
            colors, offsets, GradientInterpolationSpace.LinearSrgb, premultiplied: false);

        Assert.Multiple(() =>
        {
            Assert.That(expandedColors, Is.SameAs(colors));
            Assert.That(expandedOffsets, Is.SameAs(offsets));
        });
    }

    [TestCase(GradientInterpolationSpace.Srgb)]
    [TestCase(GradientInterpolationSpace.Oklab)]
    public void A_hard_stop_stays_hard_and_the_ends_stay_exact(GradientInterpolationSpace space)
    {
        SKColor red = new(255, 0, 0);
        SKColor blue = new(0, 0, 255);
        SKColor[] colors = [red, red, blue, blue];
        float[] offsets = [0, 0.5f, 0.5f, 1];

        (SKColor[] expandedColors, float[] expandedOffsets) = GradientStopInterpolation.Expand(
            colors, offsets, space, premultiplied: false);

        int boundary = Array.IndexOf(expandedOffsets, 0.5f);
        Assert.Multiple(() =>
        {
            Assert.That(expandedOffsets, Is.Ordered);
            Assert.That(expandedOffsets[boundary + 32], Is.EqualTo(0.5f));
            Assert.That(expandedColors[boundary - 1], Is.EqualTo(red));
            Assert.That(expandedColors[boundary + 33], Is.EqualTo(blue));
            Assert.That(expandedColors[^1], Is.EqualTo(blue));
            Assert.That(expandedOffsets[^1], Is.EqualTo(1));
        });
    }

    // Unpremultiplied, the color a fully transparent stop has shows in between, as a tint here.
    [TestCase(false, 128)]
    [TestCase(true, 255)]
    public void Premultiplied_mixing_ignores_the_color_of_a_transparent_stop(bool premultiplied, int greenInTheMiddle)
    {
        SKColor[] colors = [new SKColor(255, 0, 0, 0), SKColors.White];

        (SKColor[] expanded, _) = GradientStopInterpolation.Expand(
            colors, [0, 1], GradientInterpolationSpace.Srgb, premultiplied);

        SKColor middle = expanded[16];
        Assert.Multiple(() =>
        {
            Assert.That((int)middle.Red, Is.EqualTo(255));
            Assert.That((int)middle.Green, Is.EqualTo(greenInTheMiddle).Within(1));
            Assert.That((int)middle.Alpha, Is.EqualTo(128).Within(1));
        });
    }

    [TestCase(GradientInterpolationSpace.LinearSrgb, true)]
    [TestCase(GradientInterpolationSpace.Srgb, false)]
    [TestCase(GradientInterpolationSpace.Oklab, false)]
    [TestCase(GradientInterpolationSpace.Oklab, true)]
    public void A_color_mixed_with_itself_stays_the_same(GradientInterpolationSpace space, bool premultiplied)
    {
        SKColor color = new(123, 45, 67, 200);

        (SKColor[] expanded, _) = GradientStopInterpolation.Expand([color, color], [0, 1], space, premultiplied);

        foreach (SKColor mixed in expanded)
        {
            Assert.Multiple(() =>
            {
                Assert.That((int)mixed.Red, Is.EqualTo(123).Within(1));
                Assert.That((int)mixed.Green, Is.EqualTo(45).Within(1));
                Assert.That((int)mixed.Blue, Is.EqualTo(67).Within(1));
                Assert.That((int)mixed.Alpha, Is.EqualTo(200));
            });
        }
    }

    private static float ReadMiddle(GradientBrush brush, GradientInterpolationSpace space, int x, int y)
    {
        brush.InterpolationSpace.Value = space;
        brush.GradientStops.Add(new GradientStop(Colors.Black, 0));
        brush.GradientStops.Add(new GradientStop(Colors.White, 1));
        using Brush.Resource resource = brush.ToResource(CompositionContext.Default);
        var constructor = new BrushConstructor(
            new Rect(0, 0, Size, Size), resource, BlendMode.Src, RenderIntent.Preview, drawableBrushMaterializer: null);
        using SKShader? shader = constructor.CreateShader();
        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        using var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.RgbaF16, SKAlphaType.Premul, colorSpace));
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src })
        {
            canvas.DrawRect(SKRect.Create(Size, Size), paint);
        }

        using SKPixmap pixels = bitmap.PeekPixels();
        ReadOnlySpan<Half> values = MemoryMarshal.Cast<byte, Half>(pixels.GetPixelSpan());
        return (float)values[(y * Size + x) * 4];
    }
}
