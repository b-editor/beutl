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

    private static void RenderOnce(Drawable drawable)
    {
        using var resource = drawable.ToResource(CompositionContext.Default);
        using var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container, new PixelSize(400, 200));
        drawable.Render(context, resource);
    }

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
        Assert.DoesNotThrow(() => RenderOnce(drawable));
    }

    [Test]
    public void Spectrum_Render_NoSource_DoesNotThrow()
    {
        var drawable = CreateSpectrum();
        Assert.DoesNotThrow(() => RenderOnce(drawable));
    }

    [Test]
    public void Spectrogram_Render_NoSource_DoesNotThrow()
    {
        var drawable = CreateSpectrogram();
        Assert.DoesNotThrow(() => RenderOnce(drawable));
    }

    [TestCaseSource(nameof(WaveformShapeCases))]
    public void Waveform_WithEachShape_DoesNotThrow(Func<WaveformShape> factory)
    {
        var drawable = CreateWaveform();
        drawable.Shape.CurrentValue = factory();
        Assert.DoesNotThrow(() => RenderOnce(drawable));
    }

    public static IEnumerable<Func<WaveformShape>> WaveformShapeCases() =>
    [
        () => new MinMaxBarWaveformShape(),
        () => new LineWaveformShape(),
        () => new LineWaveformShape { Mirrored = { CurrentValue = true } },
        () => new FilledMirrorWaveformShape(),
        () => new FilledEnvelopeWaveformShape(),
        () => new FilledEnvelopeWaveformShape { Symmetric = { CurrentValue = true } },
        () => new BlockWaveformShape(),
        () => new BlockWaveformShape { Mirrored = { CurrentValue = true } },
        () => new DotsWaveformShape(),
        () => new DotsWaveformShape { Mode = { CurrentValue = DotsWaveformMode.Center } },
        () => new RadialWaveformShape(),
    ];

    [TestCase(FrequencyScale.Linear)]
    [TestCase(FrequencyScale.Logarithmic)]
    [TestCase(FrequencyScale.Mel)]
    public void Spectrum_FrequencyScale_DoesNotThrow(FrequencyScale scale)
    {
        var drawable = CreateSpectrum();
        drawable.FrequencyScale.CurrentValue = scale;
        Assert.DoesNotThrow(() => RenderOnce(drawable));
    }

    [TestCase(FrequencyScale.Linear)]
    [TestCase(FrequencyScale.Logarithmic)]
    [TestCase(FrequencyScale.Mel)]
    public void Spectrogram_FrequencyScale_DoesNotThrow(FrequencyScale scale)
    {
        var drawable = CreateSpectrogram();
        drawable.FrequencyScale.CurrentValue = scale;
        Assert.DoesNotThrow(() => RenderOnce(drawable));
    }

}
