using System.Collections.Immutable;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Effects;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class MixerLatencyAlignmentTests
{
    private const int SampleRate = 48000;
    private const float LookaheadMs = 5f;
    private const int DelaySamples = 240;
    private readonly DecoderRegistry _decoders = TestMediaHelper.CreateRegistry();

    [TestCase(44100, 220)]
    [TestCase(48000, 240)]
    public void SoundGroup_AlignsImpulsesAcrossBlocksAndThroughFinalDrain(int sampleRate, int delaySamples)
    {
        const int blockSize = 128;
        const int frames = 4 * blockSize;
        const int inlinePadding = 37;
        const int firstDrain = 19;
        const int boundaryImpulse = blockSize - 1;
        const int terminalImpulse = frames - 1;

        AudioTestWaves.WithTemporaryWave(sampleRate, frames, i => ImpulseAt(i, boundaryImpulse, terminalImpulse), source =>
        {
            SoundGroup group = CreateGroup(sampleRate, frames,
                CreateSound(source, sampleRate, frames),
                CreateSound(source, sampleRate, frames, CreateLimiter(LookaheadMs)));
            using var resource = CreateResource(group);
            using var composer = new Composer { SampleRate = sampleRate };
            var eligible = new CompositionEligibility([group]);

            for (int start = 0; start < frames; start += blockSize)
            {
                int length = start + blockSize == frames ? blockSize + inlinePadding : blockSize;
                using var block = ComposeWindow(composer, eligible, start, length, resource);
                AssertImpulses(block, start, (boundaryImpulse + delaySamples, 2));
            }

            Assert.That(composer.GetDrainLatencySamples(sampleRate), Is.EqualTo(delaySamples - inlinePadding));

            int nextStart = frames + inlinePadding;
            using var partial = composer.Flush(Range(sampleRate, nextStart, firstDrain), eligible)!;
            AssertImpulses(partial, nextStart);
            Assert.That(composer.GetDrainLatencySamples(sampleRate), Is.EqualTo(delaySamples - inlinePadding - firstDrain));

            nextStart += firstDrain;
            using var tail = composer.Flush(Range(sampleRate, nextStart, delaySamples), eligible)!;
            AssertImpulses(tail, nextStart, (terminalImpulse + delaySamples, 2));
            Assert.That(composer.GetDrainLatencySamples(sampleRate), Is.Zero);
        });
    }

    [Test]
    public void SoundGroup_DrainsDelayedDrySamplesAfterTheSlowChildEnds()
    {
        const int wetFrames = 160;
        const int dryFrames = 640;
        const int sharedImpulse = wetFrames - 1;
        const int dryImpulse = dryFrames - 1;

        AudioTestWaves.WithTemporaryWave(SampleRate, dryFrames, i => ImpulseAt(i, sharedImpulse, dryImpulse), source =>
        {
            SoundGroup group = CreateGroup(SampleRate, dryFrames,
                CreateSound(source, SampleRate, dryFrames),
                CreateSound(source, SampleRate, wetFrames, CreateLimiter(LookaheadMs)));
            using var resource = CreateResource(group);
            using var composer = new Composer { SampleRate = SampleRate };
            var eligible = new CompositionEligibility([group]);

            using var first = ComposeWindow(composer, eligible, 0, wetFrames, resource);
            AssertImpulses(first, 0);
            using var rest = ComposeWindow(composer, eligible, wetFrames, dryFrames - wetFrames, resource);
            AssertImpulses(rest, wetFrames, (sharedImpulse + DelaySamples, 2));
            Assert.That(composer.GetDrainLatencySamples(SampleRate), Is.EqualTo(DelaySamples));

            using var tail = composer.Flush(Range(SampleRate, dryFrames, DelaySamples), eligible)!;
            AssertImpulses(tail, dryFrames, (dryImpulse + DelaySamples, 1));
            Assert.That(composer.GetDrainLatencySamples(SampleRate), Is.Zero);
        });
    }

    [Test]
    public void SoundGroup_FlushDuringTheGroupRetainsTheStillPlayingChildsTail()
    {
        const int dryFrames = 160;
        const int wetFrames = 640;
        const int end = dryFrames + 37;
        const int sharedImpulse = dryFrames - 1;
        const int wetImpulse = end - 1;

        AudioTestWaves.WithTemporaryWave(SampleRate, wetFrames, i => ImpulseAt(i, sharedImpulse, wetImpulse), source =>
        {
            SoundGroup group = CreateGroup(SampleRate, wetFrames,
                CreateSound(source, SampleRate, dryFrames),
                CreateSound(source, SampleRate, wetFrames, CreateLimiter(LookaheadMs)));
            using var resource = CreateResource(group);
            using var composer = new Composer { SampleRate = SampleRate };
            var eligible = new CompositionEligibility([group]);

            using var block = ComposeWindow(composer, eligible, 0, end, resource);
            AssertImpulses(block, 0);
            Assert.That(composer.GetDrainLatencySamples(SampleRate), Is.EqualTo(DelaySamples));

            using var tail = composer.Flush(Range(SampleRate, end, DelaySamples), eligible)!;
            AssertImpulses(tail, end, (sharedImpulse + DelaySamples, 2), (wetImpulse + DelaySamples, 1));
        });
    }

    [Test]
    public void SoundGroup_AfterSeek_DropsQueuedSamplesAndAlignsTheNewInput()
    {
        const int seek = 512;
        const int blockSize = 128;
        const int oldImpulse = blockSize - 1;
        const int newImpulse = seek + 5;
        const int frames = 1024;

        AudioTestWaves.WithTemporaryWave(SampleRate, frames, i => ImpulseAt(i, oldImpulse, newImpulse), source =>
        {
            SoundGroup group = CreateGroup(SampleRate, frames,
                CreateSound(source, SampleRate, frames),
                CreateSound(source, SampleRate, frames, CreateLimiter(LookaheadMs)));
            using var resource = CreateResource(group);
            using var composer = new Composer { SampleRate = SampleRate };
            var eligible = new CompositionEligibility([group]);

            using var before = ComposeWindow(composer, eligible, 0, blockSize, resource);
            AssertImpulses(before, 0);
            using var after = ComposeWindow(composer, eligible, seek, blockSize, resource);
            AssertImpulses(after, seek);
            using var next = ComposeWindow(composer, eligible, seek + blockSize, 2 * blockSize, resource);
            AssertImpulses(next, seek + blockSize, (newImpulse + DelaySamples, 2));
        });
    }

    [Test]
    public void SoundGroup_NestedMixerReportsItsAlignedLatencyToTheParent()
    {
        const int frames = 512;
        const int inlinePadding = 37;
        const int terminalImpulse = frames - 1;

        AudioTestWaves.WithTemporaryWave(SampleRate, frames, i => ImpulseAt(i, terminalImpulse), source =>
        {
            SoundGroup child = CreateGroup(SampleRate, frames,
                CreateSound(source, SampleRate, frames),
                CreateSound(source, SampleRate, frames, CreateLimiter(LookaheadMs)));
            SoundGroup parent = CreateGroup(SampleRate, frames, child,
                CreateSound(source, SampleRate, frames, CreateLimiter(2 * LookaheadMs)));
            using var resource = CreateResource(parent);
            using var composer = new Composer { SampleRate = SampleRate };
            var eligible = new CompositionEligibility([parent]);

            using var block = ComposeWindow(composer, eligible, 0, frames + inlinePadding, resource);
            AssertImpulses(block, 0);
            Assert.That(composer.GetDrainLatencySamples(SampleRate), Is.EqualTo(2 * DelaySamples - inlinePadding));

            int tailStart = frames + inlinePadding;
            using var tail = composer.Flush(Range(SampleRate, tailStart, 2 * DelaySamples), eligible)!;
            AssertImpulses(tail, tailStart, (terminalImpulse + 2 * DelaySamples, 3));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SoundGroup_LeavesAnimatedLookaheadUncompensated(bool includeFixedLimiter)
    {
        const int frames = 1536;
        const int impulse = 61;
        AudioTestWaves.WithTemporaryWave(SampleRate, frames, i => ImpulseAt(i, impulse), source =>
        {
            LimiterEffect animated = CreateLimiter(2 * LookaheadMs);
            animated.Lookahead.SetKeyFrameAnimation(new KeyFrameAnimation<float>
            {
                KeyFrames =
                {
                    new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 2 * LookaheadMs } },
                    new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSamples(768, SampleRate) }, Value = { Value = 2 * LookaheadMs } },
                    new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSamples(frames, SampleRate) }, Value = { Value = 0f } },
                },
            });
            SoundGroup group = CreateGroup(SampleRate, frames,
                CreateSound(source, SampleRate, frames), CreateSound(source, SampleRate, frames, animated));
            if (includeFixedLimiter)
                group.Children.Add(CreateSound(source, SampleRate, frames, CreateLimiter(LookaheadMs)));

            using var resource = CreateResource(group);
            using var composer = new Composer { SampleRate = SampleRate };
            using var block = ComposeWindow(composer, new CompositionEligibility([group]), 0, 768, resource);

            (int, int) fixedPeak = includeFixedLimiter ? (impulse + DelaySamples, 2) : (impulse, 1);
            AssertImpulses(block, 0, fixedPeak, (impulse + 2 * DelaySamples, 1));
        });
    }

    [Test]
    public void SoundGroup_ZeroLatencyMixIsBitIdentical()
    {
        const int frames = 128;
        static float Sample(int ch, int i) => ((i * 17 + ch * 11) % 101 - 50) / 256f;

        AudioTestWaves.WithTemporaryWave(SampleRate, frames, Sample, source =>
        {
            SourceSound first = CreateSound(source, SampleRate, frames);
            SourceSound second = CreateSound(source, SampleRate, frames, CreateLimiter(0f));
            SourceSound third = CreateSound(source, SampleRate, frames);
            first.Gain.Value = 25f;
            second.Gain.Value = 50f;
            SoundGroup group = CreateGroup(SampleRate, frames, first, second, third);
            using var resource = CreateResource(group);
            using var composer = new Composer { SampleRate = SampleRate };
            using var block = ComposeWindow(composer, new CompositionEligibility([group]), 0, frames, resource);

            for (int ch = 0; ch < 2; ch++)
            {
                var expected = new int[frames];
                for (int i = 0; i < frames; i++)
                {
                    float sample = Sample(ch, i);
                    expected[i] = BitConverter.SingleToInt32Bits(0f + sample * 0.25f + sample * 0.5f + sample);
                }

                Assert.That(block.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits), Is.EqualTo(expected));
            }
        });
    }

    private SoundGroup.Resource CreateResource(SoundGroup group)
        => group.ToResource(new CompositionContext(MediaTime.Zero) { Decoders = _decoders });

    private static AudioBuffer ComposeWindow(Composer composer, CompositionEligibility eligibility,
        int start, int length, SoundGroup.Resource resource)
    {
        var range = Range(composer.SampleRate, start, length);
        var frame = new CompositionFrame(ImmutableArray.Create<EngineObject.Resource>(resource), range, default, eligibility);
        AudioBuffer result = composer.Compose(range, frame)!;
        Assert.That(result.SampleCount, Is.EqualTo(length));
        return result;
    }

    private static TimeRange Range(int sampleRate, int start, int length)
        => new(MediaTime.FromSamples(start, sampleRate), AudioTestContexts.ExactDuration(length, sampleRate));

    private static SoundGroup CreateGroup(int sampleRate, int frames, params Sound[] sounds)
    {
        var group = new SoundGroup { TimeRange = { Value = Range(sampleRate, 0, frames) } };
        foreach (Sound sound in sounds)
            group.Children.Add(sound);
        return group;
    }

    private static SourceSound CreateSound(SoundSource source, int sampleRate, int frames, AudioEffect? effect = null)
        => new()
        {
            Source = { Value = source },
            TimeRange = { Value = Range(sampleRate, 0, frames) },
            Effect = { Value = effect },
        };

    private static LimiterEffect CreateLimiter(float lookaheadMs)
        => new() { Lookahead = { Value = lookaheadMs }, Threshold = { Value = 0f } };

    private static (float Left, float Right) ImpulseAt(int index, int first, int second = -1)
        => index == first || index == second ? (0.125f, -0.0625f) : (0f, 0f);

    private static void AssertImpulses(AudioBuffer buffer, int start, params (int Index, int Impulses)[] peaks)
    {
        Assert.That(buffer.ChannelCount, Is.EqualTo(2));
        for (int ch = 0; ch < 2; ch++)
        {
            var expected = new float[buffer.SampleCount];
            foreach ((int index, int impulses) in peaks)
            {
                int offset = index - start;
                if (offset >= 0 && offset < buffer.SampleCount)
                    expected[offset] = (ch == 0 ? 0.125f : -0.0625f) * impulses;
            }

            Assert.That(buffer.GetChannelData(ch).ToArray(), Is.EqualTo(expected), $"Channel {ch}, start sample {start}.");
        }
    }
}
