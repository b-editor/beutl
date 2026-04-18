using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

public class AudioVisualizerDrawableTests
{
    private static AudioWaveformDrawable CreateWaveform() => new()
    {
        Width = { CurrentValue = 320f },
        Height = { CurrentValue = 80f },
        Fill = { CurrentValue = new SolidColorBrush(Colors.White) }
    };

    private static AudioSpectrumDrawable CreateSpectrum() => new()
    {
        Width = { CurrentValue = 320f },
        Height = { CurrentValue = 80f },
        Fill = { CurrentValue = new SolidColorBrush(Colors.White) }
    };

    private static AudioSpectrogramDrawable CreateSpectrogram() => new()
    {
        Width = { CurrentValue = 320f },
        Height = { CurrentValue = 80f },
        Fill = { CurrentValue = new SolidColorBrush(Colors.White) }
    };

    [Test]
    public void Waveform_NoSource_HasEmptyCache()
    {
        var drawable = CreateWaveform();
        using var resource = (AudioWaveformDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        Assert.That(resource.Width, Is.EqualTo(320f));
        Assert.That(resource.Height, Is.EqualTo(80f));
        Assert.That(resource.CachedSamples, Is.Empty);
    }

    [Test]
    public void Waveform_Render_NoSource_DoesNotThrow()
    {
        var drawable = CreateWaveform();
        using var resource = (AudioWaveformDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        using var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container, new PixelSize(400, 200));

        Assert.DoesNotThrow(() => drawable.Render(context, resource));
    }

    [Test]
    public void Spectrum_Render_NoSource_DoesNotThrow()
    {
        var drawable = CreateSpectrum();
        using var resource = (AudioSpectrumDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        using var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container, new PixelSize(400, 200));

        Assert.DoesNotThrow(() => drawable.Render(context, resource));
    }

    [Test]
    public void Spectrogram_Render_NoSource_DoesNotThrow()
    {
        var drawable = CreateSpectrogram();
        using var resource = (AudioSpectrogramDrawable.Resource)drawable.ToResource(CompositionContext.Default);

        using var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container, new PixelSize(400, 200));

        Assert.DoesNotThrow(() => drawable.Render(context, resource));
    }
}
