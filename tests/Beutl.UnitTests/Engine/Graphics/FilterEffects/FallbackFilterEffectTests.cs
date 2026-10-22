using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

// An effect whose type did not load, for example from a plugin that is not installed, is drawn as no effect.
public class FallbackFilterEffectTests
{
    private const int Size = 32;

    [Test]
    public void AShapeWithAnUnloadedEffect_IsDrawnWithoutIt()
    {
        using Bitmap plain = Render(null);
        using Bitmap withFallback = Render(new FallbackFilterEffect());

        Assert.Multiple(() =>
        {
            Assert.That(ImageMetrics.ReadPixel(plain, Size / 2, Size / 2).Alpha, Is.GreaterThan(0.95f));
            Assert.That(withFallback.GetPixelSpan().ToArray(), Is.EqualTo(plain.GetPixelSpan().ToArray()));
        });
    }

    private static Bitmap Render(FilterEffect? effect)
    {
        var shape = new RectShape();
        shape.Width.Value = Size / 2;
        shape.Height.Value = Size / 2;
        shape.Fill.Value = Brushes.White;
        shape.FilterEffect.Value = effect;
        return RenderThread.Dispatcher.Invoke(() =>
        {
            using Renderer renderer = RendererTestSupport.CreateCpuRenderer(Size, Size);
            renderer.Render(RendererTestSupport.CreateFrame(new PixelSize(Size, Size), shape));
            return renderer.Snapshot();
        });
    }
}
