using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Audio;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Editor;

[NonParallelizable]
public class SceneLatencyAlignmentTests
{
    private const int Rate = 48000;
    private const int Latency = 240;

    [TestCase(0)]
    [TestCase(37)]
    public void RawMixKeepsDelayedDrySamplesAcrossWindowsAndPartialDrains(int padding)
    {
        const int block = 128;
        const int frames = 4 * block;
        int[] impulses = [0, block - 1, frames - 1];
        WithImpulseWave(frames, impulses, source =>
        {
            Scene scene = CreateScene(frames);
            AddSound(scene, source, 0, frames);
            AddSound(scene, source, 0, frames, CreateLimiter());
            using var composer = CreateComposer(scene);
            (int, int)[] peaks = [.. impulses.Select(position => (position + Latency, 2))];

            for (int offset = 0; offset < frames; offset += block)
            {
                int length = block + (offset + block == frames ? padding : 0);
                using var audio = composer.Compose(SampleRange(offset, length))!;
                AssertImpulses(audio, offset, length, peaks);
            }

            // Both effect and mix delay have pending samples. Drain them in two parts.
            Assert.That(composer.GetDrainLatencySamples(Rate), Is.EqualTo(Latency - padding));
            const int firstDrain = 31;
            int tailStart = frames + padding;
            var eligibility = composer.Compositor.EvaluateAudio(SampleRange(tailStart, Latency)).Eligibility;
            using var partial = composer.Flush(SampleRange(tailStart, firstDrain), eligibility)!;
            AssertImpulses(partial, tailStart, firstDrain, peaks);
            Assert.That(composer.GetDrainLatencySamples(Rate), Is.EqualTo(Latency - padding - firstDrain));
            using var tail = composer.Flush(SampleRange(tailStart + firstDrain, Latency), eligibility)!;
            AssertImpulses(tail, tailStart + firstDrain, Latency, peaks);
            Assert.That(composer.GetDrainLatencySamples(Rate), Is.Zero);
        });
    }

    [Test]
    public void PreviewAndExportAlignStartBlockBoundariesSeekAndFinalTail()
    {
        const int frames = 2 * Rate + 920;
        const int sceneStart = 312;
        const int seek = 2 * Rate;
        int[] impulses = [0, Rate - 1, Rate, seek - 1, seek, frames - 1];
        WithImpulseWave(frames, impulses, source =>
        {
            Scene scene = CreateScene(frames, sceneStart);
            AddSound(scene, source, 0, frames);
            AddSound(scene, source, 0, frames, CreateLimiter());
            using var preview = CreateComposer(scene);
            MediaTime sceneEnd = SampleTime(sceneStart + frames);
            (int, int)[] peaks = [.. impulses.Select(position => (position, 2))];

            using var first = preview.ComposePlayback(SampleRange(sceneStart, Rate), sceneEnd)!;
            AssertImpulses(first, 0, Rate, peaks);
            // The first read needs part of the second input block. Seeking to the end of
            // that block must reset the graph even though the input timestamps are contiguous.
            using var afterSeek = preview.ComposePlayback(SampleRange(sceneStart + seek, frames - seek), sceneEnd)!;
            AssertImpulses(afterSeek, seek, frames - seek, peaks);

            using var restarted = preview.ComposePlayback(SampleRange(sceneStart, Rate), sceneEnd)!;
            using var second = preview.ComposePlayback(SampleRange(sceneStart + Rate, Rate), sceneEnd)!;
            using var last = preview.ComposePlayback(SampleRange(sceneStart + seek, frames - seek), sceneEnd)!;
            AssertImpulses(restarted, 0, Rate, peaks);
            AssertImpulses(second, Rate, Rate, peaks);
            AssertImpulses(last, seek, frames - seek, peaks);

            AssertExportMatches(scene, restarted, second, last);
        });
    }

    [Test]
    public void LeadingCompensationDoesNotRoundTheInputCursorAtUncommonSampleRates()
    {
        const int rate = 48001;
        const int frames = rate + 520;
        int[] impulses = [0, Latency - 1, Latency, rate - 1, rate, frames - 1];
        AudioTestWaves.WithTemporaryWave(rate, frames,
            i => impulses.Contains(i) ? (0.125f, -0.0625f) : (0f, 0f), source =>
            {
                Scene scene = CreateScene(frames, rate: rate);
                AddSound(scene, source, 0, frames, rate: rate);
                AddSound(scene, source, 0, frames, CreateLimiter(), rate);
                using var preview = CreateComposer(scene, rate);
                MediaTime second = SampleTime(rate, rate);
                MediaTime end = scene.Duration.Value;
                using var first = preview.ComposePlayback(new TimeRange(second), end)!;
                using var last = preview.ComposePlayback(TimeRange.FromRange(second, end), end)!;
                (int, int)[] peaks = [.. impulses.Select(position => (position, 2))];
                AssertImpulses(first, 0, rate, peaks);
                AssertImpulses(last, rate, frames - rate, peaks);
                AssertExportMatches(scene, first, last);
            });
    }

    [Test]
    public void ADelayedElementCanStartAfterTheFirstBlockAndEndBeforeTheScene()
    {
        const int frames = 2 * Rate + 200;
        const int wetStart = Rate + 2 * Latency;
        const int wetEnd = 2 * Rate + 13;
        int[] impulses = [12, wetStart, wetEnd - 1, wetEnd, frames - 1];
        WithImpulseWave(frames, impulses, source =>
        {
            Scene scene = CreateScene(frames);
            AddSound(scene, source, 0, frames);
            AddSound(scene, source, wetStart, wetEnd - wetStart, CreateLimiter());
            using var preview = CreateComposer(scene);
            MediaTime end = SampleTime(frames);
            (int, int)[] peaks = [(12, 1), (wetStart, 2), (wetEnd - 1, 2), (wetEnd, 1), (frames - 1, 1)];

            using var first = preview.ComposePlayback(SampleRange(0, Rate), end)!;
            using var second = preview.ComposePlayback(SampleRange(Rate, Rate), end)!;
            using var last = preview.ComposePlayback(SampleRange(2 * Rate, frames - 2 * Rate), end)!;
            AssertImpulses(first, 0, Rate, peaks);
            AssertImpulses(second, Rate, Rate, peaks);
            AssertImpulses(last, 2 * Rate, frames - 2 * Rate, peaks);
            AssertExportMatches(scene, first, second, last);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ASceneShorterThanLookaheadRetainsTheEntireClip(bool includeDry)
    {
        const int frames = 40;
        WithImpulseWave(frames, [0, frames - 1], source =>
        {
            Scene scene = CreateScene(frames);
            if (includeDry)
                AddSound(scene, source, 0, frames);
            AddSound(scene, source, 0, frames, CreateLimiter());
            using var preview = CreateComposer(scene);
            using var audio = preview.ComposePlayback(SampleRange(0, frames), SampleTime(frames))!;
            int count = includeDry ? 2 : 1;
            AssertImpulses(audio, 0, frames, (0, count), (frames - 1, count));
            AssertExportMatches(scene, audio);
        });
    }

    [Test]
    public void ZeroLatencyMixKeepsItsOriginalBitsInPreviewAndExport()
    {
        const int frames = 300;
        static float Sample(int channel, int index) => ((index * 17 + channel * 11) % 101 - 50) / 256f;

        AudioTestWaves.WithTemporaryWave(Rate, frames, Sample, source =>
        {
            Scene scene = CreateScene(frames);
            AddSound(scene, source, 0, frames);
            AddSound(scene, source, 0, frames, CreateLimiter(0f));
            using var preview = CreateComposer(scene);
            using var audio = preview.ComposePlayback(SampleRange(0, frames), SampleTime(frames))!;

            for (int ch = 0; ch < audio.ChannelCount; ch++)
            {
                var expected = new int[frames];
                for (int i = 0; i < frames; i++)
                {
                    float sample = Sample(ch, i);
                    expected[i] = BitConverter.SingleToInt32Bits(0f + sample + sample);
                }
                Assert.That(audio.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits), Is.EqualTo(expected));
            }
            AssertExportMatches(scene, audio);
        });
    }

    [Test]
    public void PlaybackLeavesAnimatedLookaheadAtItsOriginalPosition()
    {
        const int frames = 900;
        const int impulse = 60;
        WithImpulseWave(frames, [impulse], source =>
        {
            Scene scene = CreateScene(frames);
            LimiterEffect animated = CreateLimiter(10f);
            animated.Lookahead.SetKeyFrameAnimation(new KeyFrameAnimation<float>
            {
                KeyFrames =
                {
                    new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 10f } },
                    new KeyFrame<float> { KeyTime = { Value = SampleTime(frames) }, Value = { Value = 10f } },
                },
            });

            AddSound(scene, source, 0, frames);
            AddSound(scene, source, 0, frames, CreateLimiter());
            AddSound(scene, source, 0, frames, animated);
            using var preview = CreateComposer(scene);
            using var audio = preview.ComposePlayback(SampleRange(0, frames), SampleTime(frames))!;
            AssertImpulses(audio, 0, frames, (impulse, 2), (impulse + 2 * Latency, 1));
            AssertExportMatches(scene, audio);
        });
    }

    private static void AssertExportMatches(Scene scene, params AudioBuffer[] previewBlocks)
    {
        int rate = previewBlocks[0].SampleRate;
        using var composer = CreateComposer(scene, rate);
        using var progress = new Subject<MediaTime>();
        using var export = new SampleProviderImpl(scene, composer, rate, progress);
        int remaining = (int)export.SampleCount;
        List<int>[] actual = [[], []];
        while (remaining > 0)
        {
            int count = Math.Min(1024, remaining);
            using var audio = export.ReadNextSamples(count).AsTask().GetAwaiter().GetResult()!;
            for (int ch = 0; ch < audio.ChannelCount; ch++)
                actual[ch].AddRange(audio.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits));
            remaining -= count;
        }

        for (int ch = 0; ch < actual.Length; ch++)
        {
            int channel = ch;
            var expected = previewBlocks.SelectMany(buffer => buffer.GetChannelData(channel).ToArray())
                .Select(BitConverter.SingleToInt32Bits);
            Assert.That(actual[ch], Is.EqualTo(expected), $"Export channel {ch}");
        }
    }

    private static Scene CreateScene(int frames, int start = 0, int rate = Rate)
        => new(32, 32, "audio-alignment")
        {
            Start = { Value = SampleTime(start, rate) },
            Duration = { Value = AudioProcessContext.GetDurationForSampleCount(frames, rate) },
        };

    private static void AddSound(Scene scene, SoundSource source, int start, int frames, AudioEffect? effect = null,
        int rate = Rate)
    {
        var element = new Element
        {
            Start = { Value = scene.Start.Value + SampleTime(start, rate) },
            Length = { Value = AudioProcessContext.GetDurationForSampleCount(frames, rate) },
            ZIndex = { Value = scene.Children.Count },
        };
        element.AddObject(new SourceSound
        {
            Source = { Value = source },
            OffsetPosition = { Value = SampleTime(start, rate) },
            Effect = { Value = effect },
        });
        scene.Children.Add(element);
    }

    private static SceneComposer CreateComposer(Scene scene, int rate = Rate)
        => new(scene, new SceneRenderServices(TestFonts.Manager, TestMediaHelper.CreateRegistry()),
            disableResourceShare: true, forceOriginalSource: true) { SampleRate = rate };

    private static LimiterEffect CreateLimiter(float lookahead = 5f)
        => new() { Lookahead = { Value = lookahead }, Threshold = { Value = 0f } };

    private static MediaTime SampleTime(int sample, int rate = Rate) => MediaTime.FromSamples(sample, rate);

    private static TimeRange SampleRange(int start, int count) => new(SampleTime(start), SampleTime(count));

    private static void WithImpulseWave(int frames, int[] impulses, Action<SoundSource> body)
        => AudioTestWaves.WithTemporaryWave(Rate, frames,
            i => impulses.Contains(i) ? (0.125f, -0.0625f) : (0f, 0f), body);

    private static void AssertImpulses(AudioBuffer buffer, int start, int length, params (int Position, int Count)[] peaks)
    {
        Assert.That(buffer.SampleCount, Is.EqualTo(length));
        Assert.That(buffer.ChannelCount, Is.EqualTo(2));
        for (int ch = 0; ch < buffer.ChannelCount; ch++)
        {
            var expected = new float[length];
            foreach (var (position, count) in peaks)
            {
                if (position >= start && position < start + length)
                    expected[position - start] = (ch == 0 ? 0.125f : -0.0625f) * count;
            }

            Assert.That(buffer.GetChannelData(ch).ToArray(), Is.EqualTo(expected), $"Channel {ch}, start sample {start}");
        }
    }
}
