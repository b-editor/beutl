using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public sealed class BrushConstructorShaderTests
{
    // Skia CPU raster paths can round intermediate opacity one 8-bit step apart.
    [TestCase(100f)]
    [TestCase(50f)]
    [TestCase(0f)]
    public void CreateShader_MatchesThePremultipliedPaintChannels(float opacity)
    {
        var gradient = new LinearGradientBrush();
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(160, 64, 128, 192), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(80, 200, 90, 50), 1));
        gradient.Opacity.Value = opacity;
        using Brush.Resource resource = gradient.ToResource(CompositionContext.Default);
        var constructor = new BrushConstructor(
            new Rect(0, 0, 4, 1), resource, BlendMode.Src, RenderIntent.Preview, drawableBrushMaterializer: null);
        using SKShader? shader = constructor.CreateShader();
        using var bitmap = new SKBitmap(new SKImageInfo(4, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src })
        using (var configuredPaint = new SKPaint())
        {
            canvas.DrawRect(SKRect.Create(4, 1), paint);
            constructor.ConfigurePaint(configuredPaint);
            configuredPaint.IsAntialias = false;
            canvas.Translate(0, 1);
            canvas.DrawRect(SKRect.Create(4, 1), configuredPaint);
        }

        using SKPixmap pixels = bitmap.PeekPixels();
        Assert.That(pixels.GetPixelSpan().Slice(0, 16).ToArray(),
            Is.EqualTo(pixels.GetPixelSpan().Slice(pixels.RowBytes, 16).ToArray())
                .Within(opacity is > 0 and < 100 ? 1 : 0));
    }
}
