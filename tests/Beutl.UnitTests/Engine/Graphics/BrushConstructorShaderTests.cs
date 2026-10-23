using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public sealed class BrushConstructorShaderTests
{
    // Displacement maps sample the bare shader, so it has to carry the opacity a painted fill gets from the paint.
    [TestCase(100f, 255)]
    [TestCase(50f, 127)]
    [TestCase(0f, 0)]
    public void CreateShader_ScalesAGradientByItsOpacity(float opacity, int alpha)
    {
        var gradient = new LinearGradientBrush();
        gradient.GradientStops.Add(new GradientStop(Colors.White, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.White, 1));
        gradient.Opacity.Value = opacity;
        using Brush.Resource resource = gradient.ToResource(CompositionContext.Default);
        using SKShader? shader = new BrushConstructor(
                new Rect(0, 0, 4, 4), resource, BlendMode.SrcOver, RenderIntent.Preview, drawableBrushMaterializer: null)
            .CreateShader();

        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src })
        {
            canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);
        }

        Assert.That(bitmap.GetPixel(0, 0).Alpha, Is.EqualTo(alpha));
    }
}
