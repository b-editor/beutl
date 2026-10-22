using Beutl.Audio;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics;

[NonParallelizable]
public class AudioVisualizerDrawableTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void WaveformStrokeAndDots_KeepPixelsOutsideTheSignalRectangle(bool dots)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = CreateWaveform();
            drawable.Shape.Value = dots
                ? new DotsWaveformShape { DotRadius = { Value = 20 } }
                : new LineWaveformShape { Thickness = { Value = 40 } };
            drawable.Gain.Value = 100;
            AttachSyntheticSource(drawable);
            using Drawable.Resource resource = drawable.ToResource(new CompositionContext(MediaTime.FromSeconds(0.5)));
            using Bitmap bitmap = GoldenImageHarness.RenderAtScale(resource, new PixelSize(400, 200), 1);
            bool hasOutset = false;
            for (int y = 40; y < 60; y++)
                for (int x = 40; x < 360; x++)
                    hasOutset |= bitmap.SKBitmap.GetPixel(x, y).Alpha != 0;
            Assert.That(hasOutset, Is.True, "Caps/dots must survive above the nominal 80-pixel-high signal rectangle.");
        });
    }

    [Test]
    public void Spectrum_ReplayingOneResourceDoesNotAdvancePeakRelease()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = CreateSpectrum();
            AttachSyntheticSource(drawable);
            using var resource = (AudioSpectrumDrawable.Resource)drawable.ToResource(new CompositionContext(MediaTime.FromSeconds(0.5)));
            Assert.That(resource.CachedSampleLength, Is.GreaterThan(0));
            typeof(AudioSpectrumDrawable.Resource).GetField("_smoothedMagnitudes",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(resource, Enumerable.Repeat(100f, 2048).ToArray());
            using Bitmap first = GoldenImageHarness.RenderAtScale(resource, new PixelSize(320, 80), 1);
            using Bitmap second = GoldenImageHarness.RenderAtScale(resource, new PixelSize(320, 80), 1);
            Assert.That(second.GetPixelSpan<ushort>().ToArray(), Is.EqualTo(first.GetPixelSpan<ushort>().ToArray()));
        });
    }

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();
    private static AudioWaveformDrawable CreateWaveform() => new()
    {
        Width = { Value = 320f },
        Height = { Value = 80f },
        Fill = { Value = new SolidColorBrush(Colors.White) }
    };

    private static AudioSpectrumDrawable CreateSpectrum() => new()
    {
        Width = { Value = 320f },
        Height = { Value = 80f },
        Fill = { Value = new SolidColorBrush(Colors.White) }
    };

    private static AudioSpectrogramDrawable CreateSpectrogram() => new()
    {
        Width = { Value = 320f },
        Height = { Value = 80f },
        Fill = { Value = new SolidColorBrush(Colors.White) }
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
        Assert.That(resource.CachedSampleLength, Is.Zero);
    }

    // Audio visualizers must not throw at reduced/super scale output either.
    [Test]
    public void Waveform_WithEachShape_DoesNotThrow(
        [ValueSource(nameof(WaveformShapeCases))] Func<WaveformShape> factory,
        [Values(0.5f, 1f, 2f)] float outputScale)
    {
        var drawable = CreateWaveform();
        drawable.Shape.Value = factory();
        Assert.DoesNotThrow(() => RenderOnce(drawable, outputScale));
    }

    public static IEnumerable<Func<WaveformShape>> WaveformShapeCases() =>
    [
        () => new MinMaxBarWaveformShape(),
        () => new LineWaveformShape(),
        () => new LineWaveformShape { Mirrored = { Value = true } },
        () => new FilledMirrorWaveformShape(),
        () => new FilledEnvelopeWaveformShape(),
        () => new FilledEnvelopeWaveformShape { Symmetric = { Value = true } },
        () => new BlockWaveformShape(),
        () => new BlockWaveformShape { Mirrored = { Value = true } },
        () => new DotsWaveformShape(),
        () => new DotsWaveformShape { Mode = { Value = DotsWaveformMode.Center } },
        () => new RadialWaveformShape(),
    ];

    [TestCase(FrequencyScale.Linear)]
    [TestCase(FrequencyScale.Logarithmic)]
    [TestCase(FrequencyScale.Mel)]
    public void Spectrum_FrequencyScale_DoesNotThrow(FrequencyScale scale)
    {
        var drawable = CreateSpectrum();
        drawable.FrequencyScale.Value = scale;
        Assert.DoesNotThrow(() => RenderOnce(drawable));
    }

    [TestCase(FrequencyScale.Linear)]
    [TestCase(FrequencyScale.Logarithmic)]
    [TestCase(FrequencyScale.Mel)]
    public void Spectrogram_FrequencyScale_DoesNotThrow(FrequencyScale scale)
    {
        var drawable = CreateSpectrogram();
        drawable.FrequencyScale.Value = scale;
        Assert.DoesNotThrow(() => RenderOnce(drawable));
    }

    // Scale 1 is the Logarithmic case of the frequency-scale test.
    [TestCase(0.5f)]
    [TestCase(2f)]
    public void Spectrum_AtReducedScale_DoesNotThrow(float outputScale)
    {
        var drawable = CreateSpectrum();
        Assert.DoesNotThrow(() => RenderOnce(drawable, outputScale));
    }

    [TestCase(0.5f)]
    [TestCase(2f)]
    public void Spectrogram_AtReducedScale_DoesNotThrow(float outputScale)
    {
        var drawable = CreateSpectrogram();
        Assert.DoesNotThrow(() => RenderOnce(drawable, outputScale));
    }

    private static void AttachSyntheticSource(AudioVisualizerDrawable drawable)
    {
        string path = TestMediaHelper.CreateTestAudioFile(sampleRate: 44100, channels: 2, durationSeconds: 2.0);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(path));
        var sound = new SourceSound
        {
            Source = { Value = soundSource },
            // Sound.Compose clips to TimeRange, so a non-zero range covering the sample window is required.
            TimeRange = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(2)),
        };
        drawable.Source.Value = sound;
    }

    // Builds the resource at a time inside the source window, asserts the sample cache is non-empty (else the
    // render would skip the fill path and the test would be vacuous), then rasterizes at the given scale.
    private static Bitmap RenderWithSamples(AudioVisualizerDrawable drawable, PixelSize frame, float scale)
    {
        var ctx = new CompositionContext(MediaTime.FromSeconds(0.5));
        using Drawable.Resource resource = drawable.ToResource(ctx);
        var visResource = (AudioVisualizerDrawable.Resource)resource;
        Assert.That(visResource.CachedSampleLength, Is.GreaterThan(0),
            "synthetic audio composed no samples — the fill path would be skipped, making the test vacuous");

        return GoldenImageHarness.RenderAtScale(resource, frame, scale);
    }

    // With a real audio source, exercises the foreground draw + brush fill at reduced/super scale. GPU-gated.
    [TestCaseSource(nameof(WaveformShapeCases))]
    public void Waveform_WithEachShape_WithSamples_AtScale_Renders(Func<WaveformShape> factory)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = CreateWaveform();
            drawable.Shape.Value = factory();
            AttachSyntheticSource(drawable);
            Assert.DoesNotThrow(() => RenderWithSamples(drawable, new PixelSize(320, 80), 0.5f).Dispose());
            Assert.DoesNotThrow(() => RenderWithSamples(drawable, new PixelSize(320, 80), 2f).Dispose());
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
            Assert.DoesNotThrow(() => RenderWithSamples(drawable, new PixelSize(320, 80), 0.5f).Dispose());
            Assert.DoesNotThrow(() => RenderWithSamples(drawable, new PixelSize(320, 80), 2f).Dispose());
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
            Assert.DoesNotThrow(() => RenderWithSamples(drawable, new PixelSize(320, 80), 0.5f).Dispose());
            Assert.DoesNotThrow(() => RenderWithSamples(drawable, new PixelSize(320, 80), 2f).Dispose());
        });
    }

    [TestCase(500f, 0.25f)]
    [TestCase(250f, 0.5f)]
    [TestCase(125f, 1f)]
    [TestCase(50f, 2.5f)]
    public void Waveform_TransformAndOutputScaleSplits_ArePixelIdentical(
        float transformPercent,
        float outputScale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = new AudioWaveformDrawable
            {
                Width = { Value = 240f },
                Height = { Value = 120f },
                Fill = { Value = new SolidColorBrush(Colors.White) },
                Shape = { Value = new MinMaxBarWaveformShape() },
                BarCount = { Value = 64 },
                WindowSeconds = { Value = 0.1f },
            };
            AttachSyntheticSource(drawable);

            drawable.Transform.Value = new ScaleTransform(transformPercent, transformPercent);
            var transformedFrame = new PixelSize(
                (int)(400 / outputScale),
                (int)(225 / outputScale));
            using Bitmap transformed = RenderWithSamples(drawable, transformedFrame, outputScale);

            drawable.Transform.Value = new ScaleTransform(100f, 100f);
            using Bitmap reference = RenderWithSamples(drawable, new PixelSize(320, 180), 1.25f);

            GoldenImageHarness.AssertByteIdentical(reference, transformed);
        });
    }

    // Two fresh instances fed identical samples must rasterize byte-identically at density > 1,
    // guarding the rowFill loop-hoist against changing device-pixel geometry.
    [Test]
    public void Spectrogram_WithSamples_AtSuperScale_IsDeterministic()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap first = RenderFresh();
            using Bitmap second = RenderFresh();
            GoldenImageHarness.AssertByteIdentical(first, second);

            static Bitmap RenderFresh()
            {
                var drawable = CreateSpectrogram();
                AttachSyntheticSource(drawable);
                return RenderWithSamples(drawable, new PixelSize(320, 80), 2f);
            }
        });
    }
}
