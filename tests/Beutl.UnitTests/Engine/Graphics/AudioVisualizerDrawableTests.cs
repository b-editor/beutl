using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

public class AudioVisualizerDrawableTests
{
    [Test]
    public void ToResource_NoSource_HasEmptyCache()
    {
        var drawable = new AudioVisualizerDrawable();
        drawable.Width.CurrentValue = 320f;
        drawable.Height.CurrentValue = 80f;
        drawable.ForegroundColor.CurrentValue = Colors.White;
        drawable.BackgroundColor.CurrentValue = Colors.Black;

        using var resource = (AudioVisualizerDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        Assert.That(resource.Width, Is.EqualTo(320f));
        Assert.That(resource.Height, Is.EqualTo(80f));
        Assert.That(resource.CachedSamples, Is.Empty);
    }

    [Test]
    public void MeasureCore_ReturnsWidthHeight()
    {
        var drawable = new AudioVisualizerDrawable();
        drawable.Width.CurrentValue = 640f;
        drawable.Height.CurrentValue = 120f;

        using var resource = (AudioVisualizerDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        using var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container, new PixelSize(1280, 720));
        drawable.Render(context, resource);

        Assert.That(resource.Width, Is.EqualTo(640f));
        Assert.That(resource.Height, Is.EqualTo(120f));
    }

    [TestCase(AudioVisualizerMode.Waveform)]
    [TestCase(AudioVisualizerMode.Spectrum)]
    [TestCase(AudioVisualizerMode.Spectrogram)]
    public void Render_NoSource_DoesNotThrow(AudioVisualizerMode mode)
    {
        var drawable = new AudioVisualizerDrawable
        {
            Width = { CurrentValue = 200f },
            Height = { CurrentValue = 100f },
            DisplayMode = { CurrentValue = mode },
            BackgroundColor = { CurrentValue = Colors.Transparent }
        };

        using var resource = (AudioVisualizerDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        using var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container, new PixelSize(400, 200));

        Assert.DoesNotThrow(() => drawable.Render(context, resource));
    }
}
