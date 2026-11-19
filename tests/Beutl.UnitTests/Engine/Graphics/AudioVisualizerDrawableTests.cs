using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Audio;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics;

[NonParallelizable]
public class AudioVisualizerDrawableTests
{
    private readonly DecoderRegistry _decoders = TestMediaHelper.CreateRegistry();

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
            using Drawable.Resource resource = drawable.ToResource(new CompositionContext(MediaTime.FromSeconds(0.5)) { Decoders = _decoders });
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
            using var resource = (AudioSpectrumDrawable.Resource)drawable.ToResource(new CompositionContext(MediaTime.FromSeconds(0.5)) { Decoders = _decoders });
            Assert.That(resource.CachedSampleLength, Is.GreaterThan(0));
            typeof(AudioSpectrumDrawable.Resource).GetField("_smoothedMagnitudes",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(resource, Enumerable.Repeat(100f, 2048).ToArray());
            using Bitmap first = GoldenImageHarness.RenderAtScale(resource, new PixelSize(320, 80), 1);
            using Bitmap second = GoldenImageHarness.RenderAtScale(resource, new PixelSize(320, 80), 1);
            Assert.That(second.GetPixelSpan<ushort>().ToArray(), Is.EqualTo(first.GetPixelSpan<ushort>().ToArray()));
        });
    }

    // The bars at a time are the same however the frame is reached: played up to it at the scene's frame rate, drawn on
    // their own as render_still does, played with frames dropped, or reached backwards as a reversed clip plays (#4207).
    // A tone that stops leaves the bars falling for a while, which is where they depended on the frames drawn before.
    [TestCase(85f)]
    [TestCase(95f)]
    public void Spectrum_BarsAtATimeDoNotDependOnHowTheFrameIsReached(float smoothing)
    {
        const int Rate = 44100;
        // Six frames after the tone stops at 0.5 s.
        const long Target = 21;
        var frameRate = new Rational(30, 1);
        MediaTime At(long frame) => MediaTime.FromFrames(frame, frameRate);
        static (float, float) ToneThenSilence(int i)
        {
            float value = i < Rate / 2 ? 0.5f * MathF.Sin(2 * MathF.PI * 1000 * i / Rate) : 0f;
            return (value, value);
        }

        AudioTestWaves.WithTemporaryWave(Rate, Rate * 2, ToneThenSilence, source =>
        {
            AudioSpectrumDrawable.Resource Reach(params MediaTime[] times)
            {
                var drawable = CreateSpectrum();
                drawable.Smoothing.Value = smoothing;
                drawable.Source.Value = new SourceSound
                {
                    Source = { Value = source },
                    TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(2)) },
                };
                var resource = (AudioSpectrumDrawable.Resource)drawable.ToResource(
                    new CompositionContext(times[0]) { Decoders = _decoders, FrameRate = frameRate });
                foreach (MediaTime time in times.Skip(1))
                {
                    bool versionBumped = false;
                    resource.Reconcile(drawable, new CompositionContext(time) { Decoders = _decoders, FrameRate = frameRate }, ref versionBumped);
                }

                return resource;
            }

            using var played = Reach([.. Enumerable.Range(0, (int)Target + 1).Select(f => At(f))]);
            using var still = Reach(At(Target));
            using var dropping = Reach([.. Enumerable.Range(1, 11).Select(f => At(f * 2 - 1))]);
            using var reversed = Reach([.. Enumerable.Range((int)Target, 19).Reverse().Select(f => At(f))]);
            MediaTime between = At(Target) + MediaTime.FromSeconds(0.01);
            using var playedOn = Reach([.. Enumerable.Range(0, (int)Target + 1).Select(f => At(f)), between]);
            using var stillBetween = Reach(between);

            float[] expected = played.Bars.ToArray();
            Assert.That(expected.Max(), Is.GreaterThan(0.2f), "The bars are still falling from the tone.");
            Assert.Multiple(() =>
            {
                Assert.That(still.Bars.ToArray(), Is.EqualTo(expected).Within(0.01f), "drawn on its own");
                Assert.That(dropping.Bars.ToArray(), Is.EqualTo(expected).Within(0.01f), "with every other frame dropped");
                Assert.That(reversed.Bars.ToArray(), Is.EqualTo(expected).Within(0.01f), "reached backwards");
                Assert.That(stillBetween.Bars.ToArray(), Is.EqualTo(playedOn.Bars.ToArray()).Within(0.01f), "between frames");
            });
        });
    }

    // A lower floor or a higher gain shows older peaks, so a hold made with a shorter history is made again from further
    // back when either changes, even at the same time.
    [TestCase(-20f, 4f, -80f, 4f)]
    [TestCase(-80f, 0.01f, -80f, 4f)]
    public void Spectrum_ShowingOlderPeaksMakesTheHoldAgain(
        float floorBefore, float gainBefore, float floorAfter, float gainAfter)
    {
        // Fifty frames after the tone stops, past the history before the change and within the one after it.
        MediaTime target = MediaTime.FromFrames(65, s_spectrumFrameRate);
        AudioTestWaves.WithTemporaryWave(SpectrumRate, SpectrumRate * 3, ToneThen(0f), source =>
        {
            AudioSpectrumDrawable drawable = CreateSpectrum(source, 3);
            drawable.FloorDb.Value = floorBefore;
            drawable.Gain.Value = gainBefore;
            using var resource = ReachSpectrum(drawable, s_spectrumFrameRate, target);
            drawable.FloorDb.Value = floorAfter;
            drawable.Gain.Value = gainAfter;
            ReachAgain(resource, drawable, s_spectrumFrameRate, target);
            using var fresh = ReachSpectrum(drawable, s_spectrumFrameRate, target);

            float[] expected = fresh.Bars.ToArray();
            Assert.That(expected.Max(), Is.GreaterThan(0.05f), "The tone still shows after the change.");
            Assert.That(resource.Bars.ToArray(), Is.EqualTo(expected).Within(0.01f));
        });
    }

    // The bars follow the frame rate, which no property reports, so making them again at another rate at the same time
    // tells the renderer that they changed.
    [Test]
    public void Spectrum_AnotherFrameRateAtTheSameTimeChangesTheBarsAndTheVersion()
    {
        MediaTime target = MediaTime.FromSeconds(0.7);
        AudioTestWaves.WithTemporaryWave(SpectrumRate, SpectrumRate * 2, ToneThen(0f), source =>
        {
            AudioSpectrumDrawable drawable = CreateSpectrum(source, 2);
            using var resource = ReachSpectrum(drawable, new Rational(30, 1), target);
            float[] at30 = resource.Bars.ToArray();
            int version = resource.Version;
            ReachAgain(resource, drawable, new Rational(24, 1), target);
            using var fresh = ReachSpectrum(drawable, new Rational(24, 1), target);

            Assert.Multiple(() =>
            {
                Assert.That(fresh.Bars.ToArray(), Is.Not.EqualTo(at30).Within(0.01f), "The rate changes the bars.");
                Assert.That(resource.Bars.ToArray(), Is.EqualTo(fresh.Bars.ToArray()).Within(0.01f));
                Assert.That(resource.Version, Is.GreaterThan(version));
            });
        });
    }

    // A delay starts again on a window that does not follow the last one, as each frame's window does not, so the frames
    // before a still are composed one window at a time, as playing composes them.
    [Test]
    public void Spectrum_ADelayGivesTheSameBarsDrawnOnItsOwnAsPlayed()
    {
        const int Target = 21;
        AudioTestWaves.WithTemporaryWave(SpectrumRate, SpectrumRate * 2, ToneThen(0f), source =>
        {
            AudioSpectrumDrawable drawable = CreateSpectrum(source, 2);
            ((SourceSound)drawable.Source.Value!).Effect.Value = new DelayEffect();
            using var played = ReachSpectrum(drawable, s_spectrumFrameRate, Frames(0, Target));
            using var still = ReachSpectrum(drawable, s_spectrumFrameRate, MediaTime.FromFrames(Target, s_spectrumFrameRate));

            float[] expected = played.Bars.ToArray();
            Assert.That(expected.Max(), Is.GreaterThan(0.2f), "The bars are still falling from the tone.");
            Assert.That(still.Bars.ToArray(), Is.EqualTo(expected).Within(0.01f));
        });
    }

    // The hold adds each window to what is left of earlier peaks, so a peak older than the frames a hold made again
    // looks back to still lifts a bar near the floor while playing. The history reaches back until that lift is under
    // a hundredth of a bar. The quiet tone after the loud one steps through the floor in 1.5 dB steps.
    [Test]
    public void Spectrum_ABarNearTheFloorAfterAPeakIsTheSameHoweverItIsReached()
    {
        // Sixty frames after the loud tone stops, and a hundred.
        foreach (int target in new[] { 75, 115 })
        {
            for (int step = -8; step <= 8; step++)
            {
                float quiet = 2e-4f * MathF.Pow(10, step * 1.5f / 20);
                AudioTestWaves.WithTemporaryWave(SpectrumRate, SpectrumRate * 4, ToneThen(quiet), source =>
                {
                    AudioSpectrumDrawable drawable = CreateSpectrum(source, 4);
                    using var played = ReachSpectrum(drawable, s_spectrumFrameRate, Frames(0, target));
                    using var still = ReachSpectrum(
                        drawable, s_spectrumFrameRate, MediaTime.FromFrames(target, s_spectrumFrameRate));

                    Assert.That(still.Bars.ToArray(), Is.EqualTo(played.Bars.ToArray()).Within(0.01f),
                        $"frame {target}, quiet tone at {quiet}");
                });
            }
        }
    }

    // A composer carries a delay's line into a range that follows on from the last one it composed. The frames before a
    // time between frames are composed apart from the window drawn, so a window that starts where one of them ended is
    // composed as it is on its own.
    [Test]
    public void Spectrum_ComposingTheFramesBeforeLeavesTheNextWindowAlone()
    {
        MediaTime first = MediaTime.FromSeconds(0.51);
        // This window starts at 0.5 s, where the window of frame 15, composed for the first time, ends.
        MediaTime second = MediaTime.FromSeconds(0.5) + MediaTime.FromSeconds(1024.0 / SpectrumRate);
        AudioTestWaves.WithTemporaryWave(SpectrumRate, SpectrumRate * 2, ToneThen(0f), source =>
        {
            AudioSpectrumDrawable drawable = CreateSpectrum(source, 2);
            drawable.Smoothing.Value = 0;
            ((SourceSound)drawable.Source.Value!).Effect.Value = new DelayEffect
            {
                DelayTime = { Value = 10 },
                Feedback = { Value = 0 },
                DryMix = { Value = 0 },
                WetMix = { Value = 100 },
            };
            using var reached = ReachSpectrum(drawable, s_spectrumFrameRate, first, second);
            using var fresh = ReachSpectrum(drawable, s_spectrumFrameRate, second);

            Assert.That(reached.Bars.ToArray(), Is.EqualTo(fresh.Bars.ToArray()).Within(0.01f));
        });
    }

    // Smoothing 99 at a gain of 1000 would keep a peak showing for minutes, longer than a hold made again looks back.
    // The peak falls faster instead, within that history, so playing on forgets it when a hold made again does.
    [Test]
    public void Spectrum_APeakThatWouldOutlastTheHistoryFallsWithinIt()
    {
        var frameRate = new Rational(10, 1);
        AudioTestWaves.WithTemporaryWave(SpectrumRate, SpectrumRate * 32, ToneThen(0f), source =>
        {
            AudioSpectrumDrawable drawable = CreateSpectrum(source, 32);
            drawable.Smoothing.Value = 99;
            drawable.Gain.Value = 1000;
            // Ten seconds after the tone, and past the thirty seconds a hold made again looks back.
            foreach (int target in new[] { 100, 315 })
            {
                using var played = ReachSpectrum(drawable, frameRate,
                    [.. Enumerable.Range(0, target + 1).Select(f => MediaTime.FromFrames(f, frameRate))]);
                using var still = ReachSpectrum(drawable, frameRate, MediaTime.FromFrames(target, frameRate));

                float[] expected = played.Bars.ToArray();
                if (target == 100)
                    Assert.That(expected.Max(), Is.GreaterThan(0.2f), "The peak still shows ten seconds on.");
                Assert.That(still.Bars.ToArray(), Is.EqualTo(expected).Within(0.01f), $"frame {target}");
            }
        });
    }

    // A peak that would outlast the history falls at a rate the gain and the floor set, so changing either at the same
    // time makes the hold again, though it looks back no further than before.
    [TestCase(1f, -80f, 4f, -80f)]
    [TestCase(1f, -80f, 1f, -60f)]
    public void Spectrum_AGainOrAFloorThatChangesTheFallMakesTheHoldAgain(
        float gainBefore, float floorBefore, float gainAfter, float floorAfter)
    {
        var frameRate = new Rational(10, 1);
        // Nine and a half seconds after the tone stops. Both settings would hold its peak longer than thirty seconds.
        MediaTime target = MediaTime.FromFrames(100, frameRate);
        AudioTestWaves.WithTemporaryWave(SpectrumRate, SpectrumRate * 11, ToneThen(0f), source =>
        {
            AudioSpectrumDrawable drawable = CreateSpectrum(source, 11);
            drawable.Smoothing.Value = 99;
            drawable.Gain.Value = gainBefore;
            drawable.FloorDb.Value = floorBefore;
            using var resource = ReachSpectrum(drawable, frameRate, target);
            drawable.Gain.Value = gainAfter;
            drawable.FloorDb.Value = floorAfter;
            ReachAgain(resource, drawable, frameRate, target);
            using var fresh = ReachSpectrum(drawable, frameRate, target);

            float[] expected = fresh.Bars.ToArray();
            Assert.That(expected.Max(), Is.GreaterThan(0.2f), "The peak still shows after the change.");
            Assert.That(resource.Bars.ToArray(), Is.EqualTo(expected).Within(0.01f));
        });
    }

    private const int SpectrumRate = 44100;
    private static readonly Rational s_spectrumFrameRate = new(30, 1);

    // A 1 kHz tone at 0.5 for the first half second, then the same tone at the given level.
    private static Func<int, (float, float)> ToneThen(float level) => i =>
    {
        float value = (i < SpectrumRate / 2 ? 0.5f : level) * MathF.Sin(2 * MathF.PI * 1000 * i / SpectrumRate);
        return (value, value);
    };

    private static MediaTime[] Frames(int first, int last)
        => [.. Enumerable.Range(first, last - first + 1).Select(f => MediaTime.FromFrames(f, s_spectrumFrameRate))];

    private static AudioSpectrumDrawable CreateSpectrum(SoundSource source, int seconds)
    {
        AudioSpectrumDrawable drawable = CreateSpectrum();
        drawable.Source.Value = new SourceSound
        {
            Source = { Value = source },
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(seconds)) },
        };
        return drawable;
    }

    private static AudioSpectrumDrawable.Resource ReachSpectrum(
        AudioSpectrumDrawable drawable, Rational frameRate, params MediaTime[] times)
    {
        var resource = (AudioSpectrumDrawable.Resource)drawable.ToResource(
            new CompositionContext(times[0]) { Decoders = TestMediaHelper.CreateRegistry(), FrameRate = frameRate });
        foreach (MediaTime time in times.Skip(1))
            ReachAgain(resource, drawable, frameRate, time);
        return resource;
    }

    private static void ReachAgain(
        AudioSpectrumDrawable.Resource resource, AudioSpectrumDrawable drawable, Rational frameRate, MediaTime time)
    {
        bool versionBumped = false;
        resource.Reconcile(drawable, new CompositionContext(time) { Decoders = TestMediaHelper.CreateRegistry(), FrameRate = frameRate }, ref versionBumped);
    }

    private const int WindowTestRate = 44100;
    private const int WindowTestFrames = WindowTestRate * 4;
    // Far enough from both ends of the ramp for every window below.
    private const int WindowTestFrame = WindowTestRate * 5 / 2;

    // The FftSize samples that end at the current time.
    [Test]
    public void Spectrum_NewResource_ComposesTheWindowOfItsFftSize()
    {
        var drawable = CreateSpectrum();
        drawable.FftSize.Value = 4096;
        AssertComposedWindow(drawable, WindowTestFrame - 4096, 4096);
    }

    // WindowSeconds centered on the current time.
    [Test]
    public void Waveform_NewResource_ComposesTheWindowOfItsWindowSeconds()
    {
        var drawable = CreateWaveform();
        drawable.WindowSeconds.Value = 1f;
        AssertComposedWindow(drawable, WindowTestFrame - WindowTestRate / 2, WindowTestRate);
    }

    // WindowSeconds that end at the current time.
    [Test]
    public void Spectrogram_NewResource_ComposesTheWindowOfItsWindowSeconds()
    {
        var drawable = CreateSpectrogram();
        drawable.WindowSeconds.Value = 1.5f;
        AssertComposedWindow(drawable, WindowTestFrame - WindowTestRate * 3 / 2, WindowTestRate * 3 / 2);
    }

    // The same order made an edit reach the window one reconcile late, after the base had composed the old one.
    [Test]
    public void Spectrum_EditedFftSize_ComposesTheNewWindowOnTheNextReconcile()
    {
        var drawable = CreateSpectrum();
        drawable.FftSize.Value = 4096;
        AssertComposedWindow(drawable, WindowTestFrame - 2048, 2048, edit: () => drawable.FftSize.Value = 2048);
    }

    [Test]
    public void Waveform_EditedWindowSeconds_ComposesTheNewWindowOnTheNextReconcile()
    {
        var drawable = CreateWaveform();
        drawable.WindowSeconds.Value = 1f;
        AssertComposedWindow(drawable, WindowTestFrame - WindowTestRate / 4, WindowTestRate / 2,
            edit: () => drawable.WindowSeconds.Value = 0.5f);
    }

    // render_still builds a new resource for every frame, and the base used to compose the window before the derived
    // resource had set the property that sizes it (#4203). With an edit, the resource reconciles again at the same
    // time, so only the edit can change the window. A ramp gives every sample its own value, so the cache shows
    // where the window starts as well as how long it is.
    private static void AssertComposedWindow(
        AudioVisualizerDrawable drawable, int firstFrame, int frameCount, Action? edit = null)
    {
        static float Ramp(int i) => (float)i / WindowTestFrames;
        AudioTestWaves.WithTemporaryWave(WindowTestRate, WindowTestFrames, i => (Ramp(i), Ramp(i)), source =>
        {
            drawable.Source.Value = new SourceSound
            {
                Source = { Value = source },
                TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSamples(WindowTestFrames, WindowTestRate)) },
            };
            var context = new CompositionContext(MediaTime.FromSamples(WindowTestFrame, WindowTestRate)) { Decoders = TestMediaHelper.CreateRegistry() };
            using var resource = (AudioVisualizerDrawable.Resource)drawable.ToResource(context);
            if (edit != null)
            {
                edit();
                bool versionBumped = false;
                resource.Reconcile(drawable, context, ref versionBumped);
            }

            // CachedSampleSpan is protected, so read the array behind it.
            float[] cache = (float[])typeof(AudioVisualizerDrawable.Resource)
                .GetField("_cachedSamples", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(resource)!;
            float[] samples = cache[..resource.CachedSampleLength];
            Assert.That(samples, Is.Not.Empty);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(samples.Length, Is.EqualTo(frameCount).Within(1));
                Assert.That(samples[0] * WindowTestFrames, Is.EqualTo(firstFrame).Within(1));
                Assert.That(samples[^1] * WindowTestFrames, Is.EqualTo(firstFrame + frameCount - 1).Within(1));
            }
        });
    }

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
        using var resource = drawable.ToResource(new CompositionContext(MediaTime.Zero) { Decoders = TestMediaHelper.CreateRegistry() });
        using var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container, new Size(400, 200), outputScale);
        resource.Render(context);
    }

    [Test]
    public void Waveform_NoSource_HasEmptyCache()
    {
        var drawable = CreateWaveform();
        using var resource = (AudioWaveformDrawable.Resource)drawable.ToResource(new CompositionContext(MediaTime.Zero) { Decoders = _decoders });

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
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(2)) },
        };
        drawable.Source.Value = sound;
    }

    // Builds the resource at a time inside the source window, asserts the sample cache is non-empty (else the
    // render would skip the fill path and the test would be vacuous), then rasterizes at the given scale.
    private static Bitmap RenderWithSamples(AudioVisualizerDrawable drawable, PixelSize frame, float scale)
    {
        var ctx = new CompositionContext(MediaTime.FromSeconds(0.5)) { Decoders = TestMediaHelper.CreateRegistry() };
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
