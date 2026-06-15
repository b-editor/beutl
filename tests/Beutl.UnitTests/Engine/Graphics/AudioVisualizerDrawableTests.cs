using Beutl.Audio;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics;

[NonParallelizable]
public class AudioVisualizerDrawableTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();
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

    private static void RenderOnce(Drawable drawable, float outputScale = 1f)
    {
        using var resource = drawable.ToResource(CompositionContext.Default);
        using var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container, new Size(400, 200), outputScale);
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

    // feature 003 (FR-030): audio visualizers render as logical-space CTM geometry (the "leave-unchanged"
    // bucket: brushes built from canvas.Density, logical bars/lines scaled by the root CTM). These assert
    // reduced-/super-scale output-scale plumbs through every waveform shape and both spectrum drawables
    // without throwing — nothing in the visualizer path reads a device-pixel dimension that breaks at w != 1.
    // A perceptual reduced-scale gate needs an audio source + GPU (golden-suite follow-up).
    [TestCaseSource(nameof(WaveformShapeCases))]
    public void Waveform_WithEachShape_AtReducedScale_DoesNotThrow(Func<WaveformShape> factory)
    {
        var drawable = CreateWaveform();
        drawable.Shape.CurrentValue = factory();
        Assert.DoesNotThrow(() => RenderOnce(drawable, 0.5f));
        Assert.DoesNotThrow(() => RenderOnce(drawable, 2f));
    }

    [Test]
    public void Spectrum_AtReducedScale_DoesNotThrow()
    {
        var drawable = CreateSpectrum();
        Assert.DoesNotThrow(() => RenderOnce(drawable, 0.5f));
        Assert.DoesNotThrow(() => RenderOnce(drawable, 2f));
    }

    [Test]
    public void Spectrogram_AtReducedScale_DoesNotThrow()
    {
        var drawable = CreateSpectrogram();
        Assert.DoesNotThrow(() => RenderOnce(drawable, 0.5f));
        Assert.DoesNotThrow(() => RenderOnce(drawable, 2f));
    }

    // The no-source cases above build the tree with an EMPTY sample cache, so each shape's RenderForeground
    // early-returns (CachedSampleLength == 0) and the feature-003 fill path never runs. These cases attach a
    // synthetic SourceSound (a 440 Hz tone via the test decoder) and rasterize at reduced/super scale through
    // the real ImmediateCanvas, exercising the foreground draw + brush fill that plumb canvas.Density /
    // MaxWorkingScale (FR-030). GPU-gated.
    private static void AttachSyntheticSource(AudioVisualizerDrawable drawable)
    {
        string path = TestMediaHelper.CreateTestAudioFile(sampleRate: 44100, channels: 2, durationSeconds: 2.0);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(path));
        var sound = new SourceSound
        {
            Source = { CurrentValue = soundSource },
            // Sound.Compose clips to TimeRange, so a non-zero range covering the sample window is required.
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2)),
        };
        drawable.Source.CurrentValue = sound;
    }

    // Builds the resource at a time inside the source window, asserts the sample cache is non-empty (else the
    // render would skip the fill path and the test would be vacuous), then rasterizes at the given scale.
    private static void RenderWithSamplesAtScale(AudioVisualizerDrawable drawable, float scale)
    {
        var ctx = new CompositionContext(TimeSpan.FromSeconds(0.5));
        using Drawable.Resource resource = drawable.ToResource(ctx);
        var visResource = (AudioVisualizerDrawable.Resource)resource;
        Assert.That(visResource.CachedSampleLength, Is.GreaterThan(0),
            "synthetic audio composed no samples — the fill path would be skipped, making the test vacuous");

        using Bitmap _ = GoldenImageHarness.RenderAtScale(resource, new PixelSize(320, 80), scale);
    }

    [TestCaseSource(nameof(WaveformShapeCases))]
    public void Waveform_WithEachShape_WithSamples_AtScale_Renders(Func<WaveformShape> factory)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = CreateWaveform();
            drawable.Shape.CurrentValue = factory();
            AttachSyntheticSource(drawable);
            Assert.DoesNotThrow(() => RenderWithSamplesAtScale(drawable, 0.5f));
            Assert.DoesNotThrow(() => RenderWithSamplesAtScale(drawable, 2f));
        });
    }

    [Test]
    public void Spectrum_WithSamples_AtScale_Renders()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = CreateSpectrum();
            AttachSyntheticSource(drawable);
            Assert.DoesNotThrow(() => RenderWithSamplesAtScale(drawable, 0.5f));
            Assert.DoesNotThrow(() => RenderWithSamplesAtScale(drawable, 2f));
        });
    }

    [Test]
    public void Spectrogram_WithSamples_AtScale_Renders()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = CreateSpectrogram();
            AttachSyntheticSource(drawable);
            Assert.DoesNotThrow(() => RenderWithSamplesAtScale(drawable, 0.5f));
            Assert.DoesNotThrow(() => RenderWithSamplesAtScale(drawable, 2f));
        });
    }
}
