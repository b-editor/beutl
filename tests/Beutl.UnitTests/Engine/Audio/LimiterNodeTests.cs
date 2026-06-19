using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

using static Beutl.Audio.Effects.LimiterParameters;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class LimiterNodeTests
{
    private const int SampleRate = 48000;

    // A fixed non-zero lookahead used as the default for these tests, deliberately decoupled from
    // DefaultLookaheadMs (which is 0 for sample-accurate A/V sync). Keeping a non-zero
    // value here ensures the delay-line / lookahead-window behavior stays exercised regardless of
    // the production default. Tests that specifically need the zero-lookahead path pass lookaheadMs: 0f.
    private const float LookaheadMs = 5f;

    private static int LookaheadSamples(int sampleRate = SampleRate, float lookaheadMs = LookaheadMs)
        => (int)(lookaheadMs / 1000f * sampleRate);

    private static AudioProcessContext CreateContext(int sampleCount, int sampleRate = SampleRate, TimeSpan? start = null)
    {
        var duration = TimeSpan.FromSeconds((double)sampleCount / sampleRate);
        var range = new TimeRange(start ?? TimeSpan.Zero, duration);
        return new AudioProcessContext(range, sampleRate, new AnimationSampler(), null);
    }

    private static AudioBuffer CreateBuffer(int channelCount, int sampleCount, Func<int, int, float> generator, int sampleRate = SampleRate)
    {
        var buffer = new AudioBuffer(sampleRate, channelCount, sampleCount);
        for (int ch = 0; ch < channelCount; ch++)
        {
            var data = buffer.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                data[i] = generator(ch, i);
            }
        }

        return buffer;
    }

    private static LimiterNode CreateNode(
        float thresholdDb = DefaultThresholdDb,
        float releaseMs = DefaultReleaseMs,
        float lookaheadMs = LookaheadMs,
        float makeupGainDb = DefaultMakeupGainDb)
    {
        return new LimiterNode
        {
            Threshold = Property.CreateAnimatable(thresholdDb),
            Release = Property.CreateAnimatable(releaseMs),
            Lookahead = Property.CreateAnimatable(lookaheadMs),
            MakeupGain = Property.CreateAnimatable(makeupGainDb),
        };
    }

    private sealed class StubInputNode(AudioBuffer buffer) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context) => buffer;
    }

    private sealed class NullInputNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context) => null!;
    }

    [Test]
    public void Process_BelowThreshold_PassesThroughUnchanged()
    {
        const int sampleCount = 4096;
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode();
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        int lookaheadSamples = LookaheadSamples();

        // After the lookahead delay, the output should match the delayed input exactly
        // (no gain reduction applied because the signal stays below threshold).
        for (int ch = 0; ch < 2; ch++)
        {
            var inData = input.GetChannelData(ch);
            var outData = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(outData[i], Is.EqualTo(inData[i - lookaheadSamples]).Within(1e-5f),
                    $"Channel {ch} sample {i} should pass through unchanged.");
            }
        }
    }

    [Test]
    public void Process_AboveThreshold_PeakDoesNotExceedThreshold()
    {
        const int sampleCount = 4096;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: thresholdDb);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        int lookaheadSamples = LookaheadSamples();

        const float epsilon = 1e-4f;
        for (int ch = 0; ch < 2; ch++)
        {
            var outData = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(MathF.Abs(outData[i]), Is.LessThanOrEqualTo(thresholdLin + epsilon),
                    $"Channel {ch} sample {i} ({outData[i]}) exceeded threshold {thresholdLin}.");
            }
        }
    }

    [Test]
    public void Process_MakeupGain_AppliedToOutput()
    {
        const int sampleCount = 2048;
        const float makeupDb = 6f;
        float makeupLin = MathF.Pow(10f, makeupDb / 20f);

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        // Generous threshold so the limiter never engages — only makeup gain shapes the output.
        using var node = CreateNode(thresholdDb: 0f, makeupGainDb: makeupDb);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        int lookaheadSamples = LookaheadSamples();

        for (int ch = 0; ch < 2; ch++)
        {
            var inData = input.GetChannelData(ch);
            var outData = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                float expected = inData[i - lookaheadSamples] * makeupLin;
                Assert.That(outData[i], Is.EqualTo(expected).Within(1e-4f),
                    $"Channel {ch} sample {i} should equal input * makeup gain.");
            }
        }
    }

    [Test]
    public void Process_StereoLink_ReducesBothChannelsTogether()
    {
        const int sampleCount = 2048;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        // Only the left channel exceeds the threshold; the right is well below.
        using var input = CreateBuffer(2, sampleCount, (ch, i) =>
        {
            float baseSig = MathF.Sin(2f * MathF.PI * 440f * i / SampleRate);
            return ch == 0 ? 2.0f * baseSig : 0.1f * baseSig;
        });

        using var node = CreateNode(thresholdDb: thresholdDb);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        int lookaheadSamples = LookaheadSamples();

        var inR = input.GetChannelData(1);
        var outR = output.GetChannelData(1);

        // Channel-linked gain reduction must scale the right channel even though it never
        // crossed the threshold on its own.
        bool foundReduction = false;
        const float epsilon = 1e-3f;
        for (int i = lookaheadSamples; i < sampleCount; i++)
        {
            float originalRight = inR[i - lookaheadSamples];
            if (MathF.Abs(originalRight) < 0.05f) continue;

            if (MathF.Abs(outR[i]) < MathF.Abs(originalRight) - epsilon)
            {
                foundReduction = true;
                break;
            }
        }

        Assert.That(foundReduction, Is.True,
            "Right channel should be attenuated due to channel-linked gain reduction.");

        var outL = output.GetChannelData(0);
        for (int i = lookaheadSamples; i < sampleCount; i++)
        {
            Assert.That(MathF.Abs(outL[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f));
        }
    }

    [Test]
    public void Process_NonContiguousChunks_ResetsState()
    {
        const int firstSampleCount = 1024;
        using var hotInput = CreateBuffer(2, firstSampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode();
        node.AddInput(new StubInputNode(hotInput));

        var firstCtx = CreateContext(firstSampleCount, start: TimeSpan.Zero);
        using (var _ = node.Process(firstCtx)) { }

        // Replace input with silence and jump the time range so it is NOT contiguous.
        using var silence = CreateBuffer(2, firstSampleCount, (_, _) => 0f);
        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new StubInputNode(silence));

        var secondCtx = CreateContext(firstSampleCount, start: TimeSpan.FromSeconds(10));
        using var secondOut = node.Process(secondCtx);

        // After reset, no audio from the previous segment should bleed into the first
        // lookahead-window worth of samples.
        for (int ch = 0; ch < 2; ch++)
        {
            var data = secondOut.GetChannelData(ch);
            for (int i = 0; i < firstSampleCount; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(1e-6f),
                    $"Reset should clear delay-line state — channel {ch} sample {i} = {data[i]}.");
            }
        }
    }

    [Test]
    public void Process_ContiguousChunks_PreserveDelayState()
    {
        const int chunkSamples = 1024;
        int lookaheadSamples = LookaheadSamples();

        using var hotInput = CreateBuffer(2, chunkSamples,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(releaseMs: 5000f);
        node.AddInput(new StubInputNode(hotInput));

        var firstCtx = CreateContext(chunkSamples, start: TimeSpan.Zero);
        using (var _ = node.Process(firstCtx)) { }

        var secondStart = firstCtx.TimeRange.Start + firstCtx.TimeRange.Duration;
        using var silence = CreateBuffer(2, chunkSamples, (_, _) => 0f);
        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new StubInputNode(silence));

        var secondCtx = CreateContext(chunkSamples, start: secondStart);
        using var secondOut = node.Process(secondCtx);

        // The first lookahead-window worth of samples in the second chunk reads the tail of
        // the first chunk out of the delay line. If Reset() had fired erroneously the delay
        // line would have been cleared and these samples would all be zero — the non-zero
        // residual proves continuity is preserved.
        bool foundResidual = false;
        for (int ch = 0; ch < 2 && !foundResidual; ch++)
        {
            var data = secondOut.GetChannelData(ch);
            for (int i = 0; i < lookaheadSamples; i++)
            {
                if (MathF.Abs(data[i]) > 1e-3f)
                {
                    foundResidual = true;
                    break;
                }
            }
        }

        Assert.That(foundResidual, Is.True,
            "Contiguous chunk should preserve delay-line state from the previous chunk.");
    }

    [Test]
    public void Process_OneTickTimestampQuantization_PreservesDelayState()
    {
        AssertTimestampOffsetPreservesDelayState(TimeSpan.FromTicks(1), expectedResidual: true);
    }

    [Test]
    public void Process_TwoTickTimestampGap_ResetsDelayState()
    {
        AssertTimestampOffsetPreservesDelayState(TimeSpan.FromTicks(2), expectedResidual: false);
    }

    private static void AssertTimestampOffsetPreservesDelayState(TimeSpan offset, bool expectedResidual)
    {
        const int chunkSamples = 1024;
        int lookaheadSamples = LookaheadSamples();

        using var hotInput = CreateBuffer(2, chunkSamples,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        using var node = CreateNode(releaseMs: 5000f);
        node.AddInput(new StubInputNode(hotInput));

        var firstCtx = CreateContext(chunkSamples);
        using (var _ = node.Process(firstCtx)) { }

        using var silence = CreateBuffer(2, chunkSamples, (_, _) => 0f);
        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new StubInputNode(silence));

        var nextStart = firstCtx.TimeRange.Start + firstCtx.TimeRange.Duration + offset;
        using var output = node.Process(CreateContext(chunkSamples, start: nextStart));

        bool foundResidual = false;
        for (int ch = 0; ch < 2 && !foundResidual; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < lookaheadSamples; i++)
            {
                if (MathF.Abs(data[i]) > 1e-3f)
                {
                    foundResidual = true;
                    break;
                }
            }
        }

        Assert.That(foundResidual, Is.EqualTo(expectedResidual));
    }

    [Test]
    public void Process_ChannelCountChange_ReinitializesBuffers()
    {
        const int sampleCount = 1024;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var node = CreateNode(thresholdDb: thresholdDb);

        using var stereoIn = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        node.AddInput(new StubInputNode(stereoIn));

        using (var _ = node.Process(CreateContext(sampleCount, start: TimeSpan.Zero))) { }

        // Switch to mono input — buffers must be re-initialized for the new channel count.
        using var monoIn = CreateBuffer(1, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new StubInputNode(monoIn));

        using var monoOut = node.Process(CreateContext(sampleCount, start: TimeSpan.FromSeconds(1)));

        Assert.That(monoOut.ChannelCount, Is.EqualTo(1));
        int lookaheadSamples = LookaheadSamples();
        var data = monoOut.GetChannelData(0);
        for (int i = lookaheadSamples; i < sampleCount; i++)
        {
            Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f));
        }
    }

    [TestCase(44100)]
    [TestCase(48000)]
    [TestCase(96000)]
    public void Process_DifferentSampleRates_BehaveConsistently(int sr)
    {
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        int sampleCount = sr / 10; // 0.1s
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / sr),
            sampleRate: sr);

        using var node = CreateNode(thresholdDb: thresholdDb);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount, sampleRate: sr));

        Assert.That(output.SampleCount, Is.EqualTo(sampleCount), $"SR={sr} sample count mismatch");
        Assert.That(output.SampleRate, Is.EqualTo(sr));

        int lookaheadSamples = LookaheadSamples(sr);
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f),
                    $"SR={sr} channel {ch} sample {i} exceeded threshold");
            }
        }
    }

    [Test]
    public void Process_SampleRateChange_OnSameNode_ReinitializesBuffers()
    {
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var node = CreateNode(thresholdDb: thresholdDb, lookaheadMs: MaxLookaheadMs);

        using var input48 = CreateBuffer(2, 4800,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / 48000),
            sampleRate: 48000);
        node.AddInput(new StubInputNode(input48));
        using (var _ = node.Process(CreateContext(4800, sampleRate: 48000, start: TimeSpan.Zero))) { }

        // Switch the same node to a higher sample rate. The internal _maxLookaheadSamples must
        // grow to fit MaxLookaheadMs at the new rate, otherwise reads at the maximum lookahead
        // would silently return zero.
        node.RemoveInput(node.Inputs[0]);
        using var input96 = CreateBuffer(2, 9600,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / 96000),
            sampleRate: 96000);
        node.AddInput(new StubInputNode(input96));

        using var output = node.Process(CreateContext(9600, sampleRate: 96000, start: TimeSpan.FromSeconds(5)));

        Assert.That(output.SampleRate, Is.EqualTo(96000));
        int lookaheadSamples = LookaheadSamples(96000, MaxLookaheadMs);
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < 9600; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f));
            }
        }
    }

    [Test]
    public void Process_LookaheadZero_LimitsCorrectly()
    {
        const int sampleCount = 2048;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: thresholdDb, lookaheadMs: 0f);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f),
                    $"Channel {ch} sample {i} exceeded threshold with zero lookahead");
            }
        }
    }

    [Test]
    public void Process_MaxLookahead_DoesNotCollapseToSilence()
    {
        const int sampleCount = 4096;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: thresholdDb, lookaheadMs: MaxLookaheadMs);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        // Off-by-one in the lookahead clamp would collapse Read(_maxLookaheadSamples) to 0
        // (silence) for every sample. Assert non-zero output past the lookahead window.
        int lookaheadSamples = LookaheadSamples(SampleRate, MaxLookaheadMs);
        bool foundNonZero = false;
        for (int i = lookaheadSamples; i < sampleCount; i++)
        {
            if (MathF.Abs(output.GetChannelData(0)[i]) > 1e-3f)
            {
                foundNonZero = true;
                break;
            }
        }

        Assert.That(foundNonZero, Is.True, "Output collapsed to silence at MaxLookaheadMs.");

        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f));
            }
        }
    }

    [Test]
    public void Process_NegativePeak_TriggersLimiter()
    {
        const int sampleCount = 2048;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        // DC bias of -1.5 — only negative peaks exceed threshold. Catches a half-wave-
        // rectification bug where the limiter would only respond to positive samples.
        using var input = CreateBuffer(2, sampleCount, (_, _) => -1.5f);

        using var node = CreateNode(thresholdDb: thresholdDb);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        int lookaheadSamples = LookaheadSamples();
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f),
                    $"Negative peak at channel {ch} sample {i} ({data[i]}) was not limited");
            }
        }
    }

    [Test]
    public void Process_AllZeros_OutputIsAllZeros()
    {
        const int sampleCount = 1024;
        using var input = CreateBuffer(2, sampleCount, (_, _) => 0f);

        using var node = CreateNode();
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(data[i], Is.EqualTo(0f));
            }
        }
    }

    [Test]
    public void Process_MonoInput_LimitsCorrectly()
    {
        const int sampleCount = 2048;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var input = CreateBuffer(1, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: thresholdDb);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        Assert.That(output.ChannelCount, Is.EqualTo(1));

        int lookaheadSamples = LookaheadSamples();
        var data = output.GetChannelData(0);
        for (int i = lookaheadSamples; i < sampleCount; i++)
        {
            Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f));
        }
    }

    [Test]
    public void Process_ThresholdAt0Db_PassesUnitPeak()
    {
        const int sampleCount = 2048;

        // Input peak = exactly 1.0. Threshold = 0 dB → thresholdLin = 1.0.
        // Branch `windowPeak > thresholdLin` is FALSE (equal), so no reduction is applied.
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: 0f);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        int lookaheadSamples = LookaheadSamples();
        for (int ch = 0; ch < 2; ch++)
        {
            var inData = input.GetChannelData(ch);
            var outData = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(outData[i], Is.EqualTo(inData[i - lookaheadSamples]).Within(1e-4f),
                    $"Channel {ch} sample {i} should pass through unchanged at threshold = peak.");
            }
        }
    }

    [Test]
    public void Process_ThresholdAt0Db_PeakAboveOne_LimitsToOne()
    {
        const int sampleCount = 2048;
        const float thresholdLin = 1f;

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 1.5f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: 0f);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        int lookaheadSamples = LookaheadSamples();
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f));
            }
        }
    }

    [Test]
    public void Process_TinyPeakBelowThreshold_StaysFinite()
    {
        const int sampleCount = 1024;

        // windowPeak guard `> 0f` matters here: tiny non-zero input should not divide by ~0
        // and produce extreme targetGain values.
        using var input = CreateBuffer(2, sampleCount, (_, _) => 1e-12f);

        using var node = CreateNode(thresholdDb: -60f);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True);
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(1e-3f));
            }
        }
    }

    [TestCase(1f)]
    [TestCase(5f)]
    [TestCase(10f)]
    [TestCase(MaxLookaheadMs)]
    public void Process_LookaheadDelay_IsAccurate(float lookaheadMs)
    {
        // Impulse at index 0 of an otherwise silent buffer. With threshold 0 dB the signal stays below
        // the ceiling, so this is a pure passthrough delay: the impulse must re-emerge at EXACTLY index
        // lookaheadSamples (no tolerance) with its full amplitude preserved. The MaxLookaheadMs case
        // pins the boundary tap (clamped to _maxLookaheadSamples-1, the largest in-range Read offset)
        // without slack, so a one-sample off-by-one at the buffer edge would fail.
        const int sampleCount = 2048;
        using var input = CreateBuffer(1, sampleCount, (_, i) => i == 0 ? 0.5f : 0f);

        using var node = CreateNode(thresholdDb: 0f, lookaheadMs: lookaheadMs);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        int expectedDelay = LookaheadSamples(SampleRate, lookaheadMs);
        var data = output.GetChannelData(0);

        int peakIndex = 0;
        float peakValue = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            if (MathF.Abs(data[i]) > peakValue)
            {
                peakValue = MathF.Abs(data[i]);
                peakIndex = i;
            }
        }

        Assert.That(peakIndex, Is.EqualTo(expectedDelay),
            $"Impulse delay mismatch at lookaheadMs={lookaheadMs}: got {peakIndex}, expected {expectedDelay}.");
        Assert.That(peakValue, Is.EqualTo(0.5f).Within(1e-6f),
            $"Impulse amplitude not preserved at lookaheadMs={lookaheadMs}: got {peakValue}.");
    }

    private static IProperty<float> CreateAnimatedConstant(float value)
    {
        var prop = Property.CreateAnimatable(value);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            Value = value,
            KeyTime = TimeSpan.Zero,
        });
        prop.Animation = animation;
        return prop;
    }

    private static IProperty<float> CreateAnimatedRamp(float startValue, float endValue, TimeSpan duration)
    {
        var prop = Property.CreateAnimatable(startValue);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            Value = startValue,
            KeyTime = TimeSpan.Zero,
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            Value = endValue,
            KeyTime = duration,
        });
        prop.Animation = animation;
        return prop;
    }

    [Test]
    public void Process_AnimatedThreshold_LimitsCorrectly()
    {
        const int sampleCount = 4096;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = new LimiterNode
        {
            Threshold = CreateAnimatedConstant(thresholdDb),
            Release = Property.CreateAnimatable(50f),
            Lookahead = Property.CreateAnimatable(LookaheadMs),
            MakeupGain = Property.CreateAnimatable(0f),
        };
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        int lookaheadSamples = LookaheadSamples();
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f),
                    $"Animated path: channel {ch} sample {i} exceeded threshold");
            }
        }
    }

    [Test]
    public void Process_AnimatedRampedThreshold_TracksCurve()
    {
        // Threshold ramps from 0 dB at t=0 to -20 dB at t=duration. The output ceiling must
        // follow the curve — early samples allowed near 1.0, late samples capped near 0.1.
        const int sampleCount = SampleRate; // 1 second
        var duration = TimeSpan.FromSeconds(1);

        using var input = CreateBuffer(1, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = new LimiterNode
        {
            Threshold = CreateAnimatedRamp(0f, -20f, duration),
            Release = Property.CreateAnimatable(1f),
            Lookahead = Property.CreateAnimatable(LookaheadMs),
            MakeupGain = Property.CreateAnimatable(0f),
        };
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        int lookaheadSamples = LookaheadSamples();
        var data = output.GetChannelData(0);

        float earlyPeak = 0f;
        for (int i = lookaheadSamples; i < sampleCount / 8; i++)
            earlyPeak = MathF.Max(earlyPeak, MathF.Abs(data[i]));

        float latePeak = 0f;
        for (int i = (sampleCount * 7) / 8; i < sampleCount; i++)
            latePeak = MathF.Max(latePeak, MathF.Abs(data[i]));

        Assert.That(earlyPeak, Is.GreaterThan(0.5f),
            $"Early region should follow the ~0 dB threshold (got peak {earlyPeak}).");
        Assert.That(latePeak, Is.LessThan(0.2f),
            $"Late region should follow the ~-20 dB threshold (got peak {latePeak}).");
    }

    [Test]
    public void Process_LongAnimatedChunk_ProcessesAllSamples()
    {
        const int sampleCount = 5000;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = new LimiterNode
        {
            Threshold = CreateAnimatedConstant(thresholdDb),
            Release = Property.CreateAnimatable(50f),
            Lookahead = Property.CreateAnimatable(LookaheadMs),
            MakeupGain = Property.CreateAnimatable(0f),
        };
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        Assert.That(output.SampleCount, Is.EqualTo(sampleCount));

        int lookaheadSamples = LookaheadSamples();
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f));
            }
        }
    }

    [Test]
    public void Process_AnimatedExactlyChunkSize_ProcessesAllSamples()
    {
        // SampleCount equal to the internal AnimationChunkSize (1024). An off-by-one in the
        // chunk-loop bound would either skip the last batch or over-run.
        const int sampleCount = 1024;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = new LimiterNode
        {
            Threshold = CreateAnimatedConstant(thresholdDb),
            Release = Property.CreateAnimatable(50f),
            Lookahead = Property.CreateAnimatable(LookaheadMs),
            MakeupGain = Property.CreateAnimatable(0f),
        };
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        Assert.That(output.SampleCount, Is.EqualTo(sampleCount));

        int lookaheadSamples = LookaheadSamples();
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f));
            }
        }
    }

    [Test]
    public void Process_ReleaseIir_RecoversExponentially()
    {
        // Hot burst followed by silence. After the burst the gain envelope must approach 1.0
        // along an exponential curve whose time constant is set by Release. A bug that flipped
        // attack/release direction or used the wrong sign would either snap immediately or
        // never recover.
        const int burstSamples = 1024;
        const float thresholdDb = -12f;
        const float releaseMs = 100f;
        int tailSamples = (int)(SampleRate * 0.6f); // ≈ 6 release time constants
        int total = burstSamples + tailSamples;

        using var input = CreateBuffer(1, total, (_, i) =>
            i < burstSamples
                ? 2.0f * MathF.Sin(2f * MathF.PI * 1000f * i / SampleRate)
                : 0.1f * MathF.Sin(2f * MathF.PI * 1000f * i / SampleRate));

        using var node = CreateNode(thresholdDb: thresholdDb, releaseMs: releaseMs, lookaheadMs: 0f);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(total));

        var data = output.GetChannelData(0);
        var inData = input.GetChannelData(0);

        // Sample the gain envelope at ~0.5τ, ~1τ, and ~5τ into the silent tail. At each
        // non-zero input sample, gain ≈ output / input. The envelope should rise monotonically
        // and approach 1.0 within several time constants.
        int oneTau = (int)(releaseMs * 0.001f * SampleRate);
        var samplePoints = new[]
        {
            burstSamples + oneTau / 2,
            burstSamples + oneTau,
            burstSamples + oneTau * 5,
        };

        var gains = new float[samplePoints.Length];
        for (int k = 0; k < samplePoints.Length; k++)
        {
            int idx = samplePoints[k];
            // Step forward to the next non-trivial input sample so we are not dividing by ~0.
            while (idx < total - 1 && MathF.Abs(inData[idx]) < 0.05f) idx++;
            gains[k] = data[idx] / inData[idx];
        }

        Assert.That(gains[1], Is.GreaterThan(gains[0]),
            $"Gain should rise during release (early={gains[0]}, mid={gains[1]}).");
        Assert.That(gains[2], Is.GreaterThan(gains[1]),
            $"Gain should keep rising toward unity (mid={gains[1]}, late={gains[2]}).");
        Assert.That(gains[2], Is.GreaterThan(0.95f).And.LessThanOrEqualTo(1.0f + 1e-3f),
            $"Gain should approach 1.0 after ~5 time constants (got {gains[2]}).");
    }

    [Test]
    public void Process_ContiguousChunks_PreserveGainEnvelope()
    {
        // Cap a hot signal with a long release in chunk 1, then process silence in chunk 2.
        // Output[0] of chunk 2 (which uses the gain from end of chunk 1) should NOT jump back
        // to unity — that would only happen if Reset() fired erroneously between contiguous
        // chunks.
        const int chunkSamples = 1024;
        const float thresholdDb = -12f;

        using var hotInput = CreateBuffer(1, chunkSamples,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: thresholdDb, releaseMs: 5000f, lookaheadMs: 0f);
        node.AddInput(new StubInputNode(hotInput));

        var firstCtx = CreateContext(chunkSamples, start: TimeSpan.Zero);
        using var firstOut = node.Process(firstCtx);
        var firstData = firstOut.GetChannelData(0);

        // Find the last non-trivial input sample to estimate the gain at end of chunk 1.
        int lastIdx = chunkSamples - 1;
        while (lastIdx > 0 && MathF.Abs(hotInput.GetChannelData(0)[lastIdx]) < 0.5f) lastIdx--;
        float endGainChunk1 = firstData[lastIdx] / hotInput.GetChannelData(0)[lastIdx];

        Assert.That(endGainChunk1, Is.LessThan(0.7f),
            $"Sanity: limiter should be heavily attenuating at end of chunk 1 (gain={endGainChunk1}).");

        // Chunk 2: hot input again, contiguous in time. The first sample's gain should be
        // close to endGainChunk1 (release barely had time to recover), not 1.0.
        var secondStart = firstCtx.TimeRange.Start + firstCtx.TimeRange.Duration;
        node.RemoveInput(node.Inputs[0]);
        using var hotInput2 = CreateBuffer(1, chunkSamples,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        node.AddInput(new StubInputNode(hotInput2));

        using var secondOut = node.Process(CreateContext(chunkSamples, start: secondStart));
        var secondData = secondOut.GetChannelData(0);

        int firstNonTrivial = 0;
        while (firstNonTrivial < chunkSamples && MathF.Abs(hotInput2.GetChannelData(0)[firstNonTrivial]) < 0.5f) firstNonTrivial++;
        float startGainChunk2 = secondData[firstNonTrivial] / hotInput2.GetChannelData(0)[firstNonTrivial];

        Assert.That(startGainChunk2, Is.LessThan(0.9f),
            $"Gain envelope should carry across contiguous chunks (got start gain {startGainChunk2}).");
    }

    [Test]
    public void Process_NoInputs_Throws()
    {
        using var node = CreateNode();
        Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(128)));
    }

    [Test]
    public void Process_MultipleInputs_Throws()
    {
        using var input1 = CreateBuffer(2, 128, (_, _) => 0f);
        using var input2 = CreateBuffer(2, 128, (_, _) => 0f);

        using var node = CreateNode();
        node.AddInput(new StubInputNode(input1));
        node.AddInput(new StubInputNode(input2));

        Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(128)));
    }

    [Test]
    public void Process_NullUpstream_Throws()
    {
        using var node = CreateNode();
        node.AddInput(new NullInputNode());

        Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(128)));
    }

    [Test]
    public void Process_SampleRateMismatch_Throws()
    {
        using var input = CreateBuffer(2, 128, (_, _) => 0f, sampleRate: 44100);
        using var node = CreateNode();
        node.AddInput(new StubInputNode(input));

        // Context says 48000, input is 44100 — must surface as a clear error rather than
        // silently producing wrong-pitch output.
        Assert.Throws<InvalidOperationException>(
            () => node.Process(CreateContext(128, sampleRate: SampleRate)));
    }

    [Test]
    public void Process_NaNParameter_DoesNotProduceNaNOutput()
    {
        const int sampleCount = 1024;
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(float.NaN),
            Release = Property.CreateAnimatable(float.NaN),
            Lookahead = Property.CreateAnimatable(float.NaN),
            MakeupGain = Property.CreateAnimatable(float.NaN),
        };
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"Channel {ch} sample {i} = {data[i]} (NaN/Inf must not reach the output).");
            }
        }
    }

    [Test]
    public void Process_OutOfRangeParameters_AreClampedNotThrown()
    {
        const int sampleCount = 2048;
        const float thresholdLin = 1f;

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(999f),
            Release = Property.CreateAnimatable(-100f),
            Lookahead = Property.CreateAnimatable(9999f),
            MakeupGain = Property.CreateAnimatable(999f),
        };
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        float upperBound = thresholdLin * MathF.Pow(10f, MaxMakeupGainDb / 20f);
        int lookaheadSamples = LookaheadSamples(SampleRate, MaxLookaheadMs);
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = lookaheadSamples; i < sampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True);
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(upperBound + 1e-3f));
            }
        }
    }

    [Test]
    public void Process_NaNInputSamples_AreSanitizedToZero()
    {
        // Asserts that NaN/Inf input samples are sanitized; rationale lives in
        // LimiterNode.IngestSample.
        const int sampleCount = 1024;

        using var input = CreateBuffer(2, sampleCount, (ch, i) =>
        {
            if (i % 100 == 0) return float.NaN;
            if (i % 100 == 50) return float.PositiveInfinity;
            return 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate);
        });

        using var node = CreateNode();
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"Channel {ch} sample {i} = {data[i]} (NaN/Inf input must be sanitized).");
            }
        }
    }

    [Test]
    public void Process_ContiguousNonFiniteBurst_AreSanitizedToZero()
    {
        // The strided NaN/Inf test never places non-finite values on consecutive samples. A
        // realistic upstream corruption is a clustered burst; IngestSample sanitizes each sample
        // independently, so a contiguous run must still produce finite, bounded output.
        const int sampleCount = 1024;
        const int burstStart = 400;
        const int burstLength = 64;

        using var input = CreateBuffer(2, sampleCount, (_, i) =>
        {
            if (i >= burstStart && i < burstStart + burstLength)
                return (i & 1) == 0 ? float.NaN : float.PositiveInfinity;
            return 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate);
        });

        using var node = CreateNode();
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        float upperBound = MathF.Pow(10f, DefaultThresholdDb / 20f) * MathF.Pow(10f, DefaultMakeupGainDb / 20f);
        int lookaheadSamples = LookaheadSamples();
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"Channel {ch} sample {i} = {data[i]} (contiguous NaN/Inf burst must be sanitized).");
                if (i >= lookaheadSamples)
                    Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(upperBound + 1e-3f));
            }
        }
    }

    [Test]
    public void Process_NonFiniteFinalSample_IsSanitizedToZero()
    {
        // A non-finite value at the very last sample lands inside the lookahead window at the chunk
        // boundary, the one position the strided test never covers. It must still be coerced.
        const int sampleCount = 1024;

        using var input = CreateBuffer(2, sampleCount, (_, i) =>
            i == sampleCount - 1
                ? float.NaN
                : 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode();
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"Channel {ch} sample {i} = {data[i]} (non-finite final sample must be sanitized).");
            }
        }
    }

    [Test]
    public void Process_ZeroSampleCountInput_ReturnsEmptyBuffer()
    {
        using var input = new AudioBuffer(SampleRate, 2, 0);
        using var node = CreateNode();
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(0));
        Assert.That(output.SampleCount, Is.EqualTo(0));
        Assert.That(output.ChannelCount, Is.EqualTo(2));
    }

    [Test]
    public void Process_EmptyChunkBetweenContiguousChunks_PreservesDelayState()
    {
        // An empty chunk wedged between contiguous non-empty chunks must be a pure pass-through:
        // it must not call Reset() and must not advance _lastTimeRangeEnd, so the next non-empty
        // chunk is evaluated against the previous *non-empty* chunk's end and resumes
        // contiguously. A regression that called Reset() inside the empty-chunk branch would
        // wipe the delay line and zero out the lookahead window of the resume chunk.
        const int chunkSamples = 1024;
        int lookaheadSamples = LookaheadSamples();

        using var hotInput = CreateBuffer(2, chunkSamples,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(releaseMs: 5000f);
        node.AddInput(new StubInputNode(hotInput));

        var firstCtx = CreateContext(chunkSamples, start: TimeSpan.Zero);
        using (var _ = node.Process(firstCtx)) { }

        var emptyStart = firstCtx.TimeRange.Start + firstCtx.TimeRange.Duration;
        using var emptyInput = new AudioBuffer(SampleRate, 2, 0);
        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new StubInputNode(emptyInput));
        using (var _ = node.Process(CreateContext(0, start: emptyStart))) { }

        using var silence = CreateBuffer(2, chunkSamples, (_, _) => 0f);
        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new StubInputNode(silence));

        using var resumeOut = node.Process(CreateContext(chunkSamples, start: emptyStart));

        bool foundResidual = false;
        for (int ch = 0; ch < 2 && !foundResidual; ch++)
        {
            var data = resumeOut.GetChannelData(ch);
            for (int i = 0; i < lookaheadSamples; i++)
            {
                if (MathF.Abs(data[i]) > 1e-3f)
                {
                    foundResidual = true;
                    break;
                }
            }
        }

        Assert.That(foundResidual, Is.True,
            "Empty chunk at the boundary should not force a reset — delay-line residual must survive.");
    }

    [Test]
    public void Process_EmptyChunkAtDiscontinuity_DoesNotMaskFollowupReset()
    {
        // The complement of the test above: if an empty chunk is silently used to advance the
        // limiter's notion of time (e.g., a regression that did `_lastTimeRangeEnd = Start +
        // Duration` inside the empty-chunk branch), then a non-empty chunk arriving at the
        // empty chunk's position would be misclassified as contiguous and would replay stale
        // delay-line audio from before the seek — a silent failure. Verify that the next
        // non-empty chunk still triggers Reset() against the previous non-empty chunk's end.
        const int chunkSamples = 1024;

        using var hotInput = CreateBuffer(2, chunkSamples,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode();
        node.AddInput(new StubInputNode(hotInput));
        using (var _ = node.Process(CreateContext(chunkSamples, start: TimeSpan.Zero))) { }

        // Empty chunk at a position that is NOT contiguous with the first chunk.
        using var emptyInput = new AudioBuffer(SampleRate, 2, 0);
        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new StubInputNode(emptyInput));
        using (var _ = node.Process(CreateContext(0, start: TimeSpan.FromSeconds(10)))) { }

        // Resume with silence at the empty chunk's position. The empty chunk must not have
        // updated _lastTimeRangeEnd, so this chunk's Start (10s) differs from the previous
        // non-empty chunk's end (1024/SR ≈ 0.021s) and Reset() must fire — wiping the delay
        // line so the silent input produces silent output.
        using var silence = CreateBuffer(2, chunkSamples, (_, _) => 0f);
        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new StubInputNode(silence));

        using var resumeOut = node.Process(CreateContext(chunkSamples, start: TimeSpan.FromSeconds(10)));

        for (int ch = 0; ch < 2; ch++)
        {
            var data = resumeOut.GetChannelData(ch);
            for (int i = 0; i < chunkSamples; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(1e-6f),
                    $"Empty chunk must not silently advance _lastTimeRangeEnd — channel {ch} sample {i} = {data[i]}.");
            }
        }
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        var node = CreateNode();
        using var input = CreateBuffer(2, 128,
            (_, i) => 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        node.AddInput(new StubInputNode(input));
        using (var _ = node.Process(CreateContext(128))) { }

        node.Dispose();
        Assert.DoesNotThrow(() => node.Dispose(),
            "Double Dispose() must not throw — CircularBuffer state is owned by the node.");
    }

    [Test]
    public void Dispose_BeforeProcess_DoesNotThrow()
    {
        // Dispose before the first Process must not blow up — buffers were never allocated.
        var node = CreateNode();
        Assert.DoesNotThrow(() => node.Dispose());
    }

    [Test]
    public void Process_AnimatedRamp_AcrossContiguousChunks_TracksCurve()
    {
        // The single-chunk ramp test (Process_AnimatedRampedThreshold_TracksCurve) only exercises
        // the inner AnimationChunkSize loop. Splitting the same ramp into multiple contiguous
        // outer Process() calls verifies that the curve is sampled at the correct absolute time
        // in each chunk — a regression where chunkRange computation reset to t=0 per outer call
        // would make every chunk see the start-of-ramp threshold and not be detected by the
        // single-chunk test.
        const int chunkCount = 4;
        const int chunkSamples = SampleRate / chunkCount; // 0.25s each, 1s total ramp
        var totalDuration = TimeSpan.FromSeconds(1);

        var thresholdProp = CreateAnimatedRamp(0f, -20f, totalDuration);
        var releaseProp = Property.CreateAnimatable(1f);
        var lookaheadProp = Property.CreateAnimatable(LookaheadMs);
        var makeupProp = Property.CreateAnimatable(0f);

        using var node = new LimiterNode
        {
            Threshold = thresholdProp,
            Release = releaseProp,
            Lookahead = lookaheadProp,
            MakeupGain = makeupProp,
        };

        var firstPeaks = new float[chunkCount];
        var lastPeaks = new float[chunkCount];
        int lookaheadSamples = LookaheadSamples();

        for (int c = 0; c < chunkCount; c++)
        {
            using var input = CreateBuffer(1, chunkSamples,
                (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * (c * chunkSamples + i) / SampleRate));

            if (node.Inputs.Count > 0)
                node.RemoveInput(node.Inputs[0]);
            node.AddInput(new StubInputNode(input));

            var chunkStart = TimeSpan.FromTicks(totalDuration.Ticks * c / chunkCount);
            using var output = node.Process(CreateContext(chunkSamples, start: chunkStart));
            var data = output.GetChannelData(0);

            // Sample peaks in the early and late portions of each chunk (after the lookahead
            // delay so we read post-limit output, not the silence prefix on chunk 0).
            int startIdx = c == 0 ? lookaheadSamples : 0;
            int mid = chunkSamples / 2;
            float peak1 = 0f;
            for (int i = startIdx; i < mid; i++) peak1 = MathF.Max(peak1, MathF.Abs(data[i]));
            float peak2 = 0f;
            for (int i = mid; i < chunkSamples; i++) peak2 = MathF.Max(peak2, MathF.Abs(data[i]));
            firstPeaks[c] = peak1;
            lastPeaks[c] = peak2;
        }

        // Each successive chunk's late peak should be <= a chunk earlier (monotonic ramp down).
        // Allow a small slack for envelope overshoot at chunk boundaries.
        for (int c = 1; c < chunkCount; c++)
        {
            Assert.That(lastPeaks[c], Is.LessThanOrEqualTo(lastPeaks[c - 1] + 0.05f),
                $"Chunk {c} late peak ({lastPeaks[c]}) should not exceed chunk {c - 1} late peak ({lastPeaks[c - 1]}).");
        }

        // Chunk 0 (≈0 dB threshold) should pass much hotter audio than chunk 3 (≈-20 dB).
        Assert.That(firstPeaks[0], Is.GreaterThan(0.5f),
            $"First chunk should follow the ~0 dB threshold (got peak {firstPeaks[0]}).");
        Assert.That(lastPeaks[chunkCount - 1], Is.LessThan(0.2f),
            $"Last chunk should follow the ~-20 dB threshold (got peak {lastPeaks[chunkCount - 1]}).");
    }

    [Test]
    public void Process_AnimatedNonContiguousChunks_ResetsState()
    {
        // The static-path counterpart (Process_NonContiguousChunks_ResetsState) covers the
        // _lastTimeRangeEnd discontinuity branch. Repeating it under an animated path guarantees
        // that the discontinuity check is not gated on `hasAnimation == false` — a refactor
        // that moved the Reset() call into ProcessStatic would silently bypass it for animated
        // properties and let the previous segment's audio leak across seeks.
        const int firstSampleCount = 1024;
        var duration = TimeSpan.FromSeconds((double)firstSampleCount / SampleRate);

        using var hotInput = CreateBuffer(2, firstSampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = new LimiterNode
        {
            Threshold = CreateAnimatedRamp(-1f, -1f, duration),
            Release = Property.CreateAnimatable(50f),
            Lookahead = Property.CreateAnimatable(LookaheadMs),
            MakeupGain = Property.CreateAnimatable(0f),
        };
        node.AddInput(new StubInputNode(hotInput));

        var firstCtx = CreateContext(firstSampleCount, start: TimeSpan.Zero);
        using (var _ = node.Process(firstCtx)) { }

        using var silence = CreateBuffer(2, firstSampleCount, (_, _) => 0f);
        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new StubInputNode(silence));

        // Jump ten seconds — must be detected as a discontinuity even on the animated path.
        var secondCtx = CreateContext(firstSampleCount, start: TimeSpan.FromSeconds(10));
        using var secondOut = node.Process(secondCtx);

        for (int ch = 0; ch < 2; ch++)
        {
            var data = secondOut.GetChannelData(ch);
            for (int i = 0; i < firstSampleCount; i++)
            {
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(1e-6f),
                    $"Reset should clear delay-line state on the animated path — channel {ch} sample {i} = {data[i]}.");
            }
        }
    }

    [Test]
    public void LimiterEffect_CreateNode_WiresPropertiesFromEffect()
    {
        var effect = new LimiterEffect();
        var ctx = new AudioContext(SampleRate, 2);
        var input = ctx.AddNode(new StubInputNode(new AudioBuffer(SampleRate, 2, 0)));

        var node = (LimiterNode)effect.CreateNode(ctx, input);

        Assert.That(node.Threshold, Is.SameAs(effect.Threshold));
        Assert.That(node.Release, Is.SameAs(effect.Release));
        Assert.That(node.Lookahead, Is.SameAs(effect.Lookahead));
        Assert.That(node.MakeupGain, Is.SameAs(effect.MakeupGain));

        Assert.That(effect.Threshold.CurrentValue, Is.EqualTo(DefaultThresholdDb));
        Assert.That(effect.Release.CurrentValue, Is.EqualTo(DefaultReleaseMs));
        Assert.That(effect.Lookahead.CurrentValue, Is.EqualTo(DefaultLookaheadMs));
        Assert.That(effect.MakeupGain.CurrentValue, Is.EqualTo(DefaultMakeupGainDb));
    }

    [Test]
    public void LimiterEffect_DefaultLookahead_IsZeroForSampleAccurateSync()
    {
        // The default is intentionally 0 ms so that adding the effect does not shift audio or drop
        // boundary samples in a video editor that has no plugin-delay-compensation. Guards against
        // an accidental revert to a non-zero default.
        Assert.That(DefaultLookaheadMs, Is.EqualTo(0f));
        Assert.That(new LimiterEffect().Lookahead.CurrentValue, Is.EqualTo(0f));
    }

    [TestCase(-6f, 6f)]
    [TestCase(-6f, 12f)]
    public void Process_LimitingWithPositiveMakeup_ReachesThresholdPlusMakeupCeiling(float thresholdDb, float makeupDb)
    {
        // Documented contract (LimiterEffect XML): the final peak settles at Threshold + MakeupGain
        // dB — the limiter caps to Threshold, then makeup scales it back up. The second case pushes
        // the ceiling above 0 dBFS on purpose. A makeup-before-limiting bug would fail the lower bound.
        const int sampleCount = 8192;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);
        float makeupLin = MathF.Pow(10f, makeupDb / 20f);
        float ceiling = thresholdLin * makeupLin;

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: thresholdDb, makeupGainDb: makeupDb);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        float maxPeak = 0f;
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = sampleCount / 2; i < sampleCount; i++)
            {
                maxPeak = MathF.Max(maxPeak, MathF.Abs(data[i]));
            }
        }

        // Brick wall: never exceeds the ceiling (makeup only scales the capped signal).
        Assert.That(maxPeak, Is.LessThanOrEqualTo(ceiling + 1e-3f),
            $"threshold={thresholdDb}dB makeup={makeupDb}dB: {maxPeak} exceeded ceiling {ceiling}.");
        // ...and actually reaches it (guards a makeup-before-limiting or wrong-ceiling bug).
        Assert.That(maxPeak, Is.GreaterThan(ceiling * 0.95f),
            $"threshold={thresholdDb}dB makeup={makeupDb}dB: {maxPeak} did not reach ceiling {ceiling}.");
    }

    [Test]
    public void Process_ContiguousChannelCountChange_ReinitializesBuffersAndStillLimits()
    {
        // A channel-count change reallocates the per-channel buffers (InitializeBuffers) regardless of
        // time-range contiguity, so the resulting state is always fresh and there is no carried-over
        // envelope to observe (this test cannot, by design, distinguish a discontinuity reset from the
        // buffer re-init). It asserts the observable contract: across shrink (stereo->mono) and grow
        // (mono->stereo) the output adopts the new channel count, stays finite, and still limits.
        const int chunkSamples = 1024;
        const float thresholdDb = -6f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var node = CreateNode(thresholdDb: thresholdDb, lookaheadMs: 0f);

        int[] channelSequence = [2, 1, 2];
        TimeSpan start = TimeSpan.Zero;
        foreach (int channels in channelSequence)
        {
            using var input = CreateBuffer(channels, chunkSamples,
                (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
            var ctx = CreateContext(chunkSamples, start: start);
            node.ClearInputs();
            node.AddInput(new StubInputNode(input));
            using var output = node.Process(ctx);

            Assert.That(output.ChannelCount, Is.EqualTo(channels));
            for (int ch = 0; ch < channels; ch++)
            {
                var data = output.GetChannelData(ch);
                for (int i = 0; i < chunkSamples; i++)
                {
                    Assert.That(float.IsFinite(data[i]), Is.True,
                        $"channels={channels} ch={ch} sample {i} must be finite.");
                    Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f),
                        $"channels={channels} ch={ch} sample {i} must respect threshold.");
                }
            }

            start += ctx.TimeRange.Duration;
        }
    }

    [Test]
    public void Process_AnimatedLookahead_StaysFiniteAndBoundedByThreshold()
    {
        // Animating Lookahead varies the delay tap and window length per sample (the animated scan
        // path). Confirm the output stays finite and within the brick-wall ceiling throughout.
        const int sampleCount = 8192;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);
        var duration = TimeSpan.FromSeconds((double)sampleCount / SampleRate);

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(thresholdDb),
            Release = Property.CreateAnimatable(50f),
            Lookahead = CreateAnimatedRamp(0f, MaxLookaheadMs, duration),
            MakeupGain = Property.CreateAnimatable(0f),
        };
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"animated-lookahead ch={ch} sample {i} must be finite.");
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-3f),
                    $"animated-lookahead ch={ch} sample {i} must respect threshold.");
            }
        }
    }

    [Test]
    public void Process_MultiChannel_LinksGainAcrossAllChannels()
    {
        // Channel-linked detection: a peak on any single channel reduces ALL channels by the same
        // gain (preserving inter-channel ratios). Exercises N > 2 (6-channel surround).
        const int channels = 6;
        const int sampleCount = 4096;
        const int hotChannel = 3;
        const float thresholdDb = -6f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var input = CreateBuffer(channels, sampleCount, (ch, i) =>
        {
            float wave = MathF.Sin(2f * MathF.PI * 440f * i / SampleRate);
            return ch == hotChannel ? 2.0f * wave : 0.1f * wave;
        });

        using var node = CreateNode(thresholdDb: thresholdDb, lookaheadMs: 0f);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        Assert.That(output.ChannelCount, Is.EqualTo(channels));

        var hotIn = input.GetChannelData(hotChannel);
        var hotOut = output.GetChannelData(hotChannel);
        for (int i = 0; i < sampleCount; i++)
        {
            Assert.That(MathF.Abs(hotOut[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f),
                $"hot channel sample {i} must respect threshold.");

            if (MathF.Abs(hotIn[i]) > 1e-6f)
            {
                float linkedGain = hotOut[i] / hotIn[i];
                for (int ch = 0; ch < channels; ch++)
                {
                    if (ch == hotChannel) continue;
                    var quietOut = output.GetChannelData(ch)[i];
                    var quietIn = input.GetChannelData(ch)[i];
                    Assert.That(quietOut, Is.EqualTo(quietIn * linkedGain).Within(1e-4f),
                        $"channel {ch} sample {i} must share the hot channel's linked gain.");
                }
            }
        }
    }

    [Test]
    public void Process_LargeLookaheadBurst_GainRecoversAfterPeakLeavesWindow()
    {
        // Guards the static-path sliding-window-max (monotonic deque): after a short loud burst
        // leaves the lookahead window, the tracked window peak must drop so the gain recovers. A
        // deque that failed to evict the stale max would keep attenuating the trailing quiet signal.
        const int sampleCount = 16384;
        const int burstLen = 64;
        const float thresholdDb = -6f;
        const float quietAmp = 0.2f; // below thresholdLin (~0.501)

        using var input = CreateBuffer(2, sampleCount, (_, i) =>
        {
            float wave = MathF.Sin(2f * MathF.PI * 440f * i / SampleRate);
            return i < burstLen ? 4.0f * wave : quietAmp * wave;
        });

        using var node = CreateNode(thresholdDb: thresholdDb, releaseMs: 1f,
            lookaheadMs: MaxLookaheadMs);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        float tailPeak = 0f;
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = sampleCount / 2; i < sampleCount; i++)
            {
                tailPeak = MathF.Max(tailPeak, MathF.Abs(data[i]));
            }
        }

        Assert.That(tailPeak, Is.GreaterThan(0.18f),
            "Quiet tail must recover after the burst leaves the window (deque must evict the stale max).");
        Assert.That(tailPeak, Is.LessThanOrEqualTo(quietAmp + 1e-3f),
            "Quiet tail is below threshold and must not be amplified.");
    }

    // Step animation: holds `first` for the whole [0, boundary) range and switches to `second` from
    // `boundary` onward. Used to drive the animated/scan path with a per-chunk-constant lookahead so it
    // can serve as an independent oracle for the static deque path across a lookahead change.
    private static IProperty<float> CreateAnimatedStep(float first, float second, TimeSpan boundary)
    {
        var prop = Property.CreateAnimatable(first);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = first, KeyTime = TimeSpan.Zero });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = first, KeyTime = boundary - TimeSpan.FromTicks(1) });
        animation.KeyFrames.Add(new KeyFrame<float> { Value = second, KeyTime = boundary });
        prop.Animation = animation;
        return prop;
    }

    [Test]
    public void Process_StaticDeque_MatchesAnimatedScan_ForEqualConstantLookahead()
    {
        // Pins the perf commit's core correctness claim: the static path's O(1) monotonic-deque window
        // maximum (PushWindowPeak) must produce output bit-identical to the animated path's direct
        // O(lookahead) scan (ScanWindowPeak) when the lookahead is an equal constant. A constant
        // KeyFrameAnimation on Lookahead forces the animated/scan path; everything else is identical, so
        // any divergence is a deque bug. Runs multiple contiguous chunks to also cover deque persistence.
        const int chunkSamples = 2048;
        const int chunks = 3;
        const float thresholdDb = -6f;
        const float releaseMs = 80f;
        const float lookaheadMs = MaxLookaheadMs; // large window stresses the window-max

        // Deterministic, transient-rich signal so the window maximum changes often (spikes force evictions).
        static float Gen(int ch, int i)
        {
            float tone = MathF.Sin(2f * MathF.PI * 523f * i / SampleRate);
            float env = 0.6f + 0.4f * MathF.Sin(2f * MathF.PI * 3f * i / SampleRate);
            float spike = (i % 311 == 0) ? 3.0f : 0f;
            return (1.4f * tone * env + spike) * (ch == 0 ? 1.0f : 0.85f);
        }

        using var staticNode = CreateNode(thresholdDb: thresholdDb, releaseMs: releaseMs, lookaheadMs: lookaheadMs);
        using var animatedNode = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(thresholdDb),
            Release = Property.CreateAnimatable(releaseMs),
            Lookahead = CreateAnimatedConstant(lookaheadMs), // forces ProcessAnimated/ScanWindowPeak
            MakeupGain = Property.CreateAnimatable(0f),
        };

        TimeSpan start = TimeSpan.Zero;
        for (int c = 0; c < chunks; c++)
        {
            int baseIndex = c * chunkSamples;
            using var inStatic = CreateBuffer(2, chunkSamples, (ch, i) => Gen(ch, baseIndex + i));
            using var inAnimated = CreateBuffer(2, chunkSamples, (ch, i) => Gen(ch, baseIndex + i));
            var ctx = CreateContext(chunkSamples, start: start);

            staticNode.ClearInputs();
            staticNode.AddInput(new StubInputNode(inStatic));
            animatedNode.ClearInputs();
            animatedNode.AddInput(new StubInputNode(inAnimated));

            using var outStatic = staticNode.Process(ctx);
            using var outAnimated = animatedNode.Process(ctx);

            for (int ch = 0; ch < 2; ch++)
            {
                var s = outStatic.GetChannelData(ch);
                var a = outAnimated.GetChannelData(ch);
                for (int i = 0; i < chunkSamples; i++)
                {
                    Assert.That(s[i], Is.EqualTo(a[i]).Within(1e-6f),
                        $"deque vs scan mismatch chunk {c} ch {ch} sample {i}: static={s[i]} animated={a[i]}.");
                }
            }

            start += ctx.TimeRange.Duration;
        }
    }

    [Test]
    public void Process_StaticLookaheadChange_RebuildsDequeToMatchScan()
    {
        // Exercises the EnsureDeque rebuild-from-peak-ring branch: changing a STATIC lookahead between
        // two contiguous chunks on a reused node forces a deque rebuild. The reused deque node is
        // compared against an independent animated/scan node fed the identical signal with a step
        // lookahead schedule (lookA during chunk 1, lookB during chunk 2). Equal output on chunk 2
        // proves the rebuild reconstructs the correct window state for the new lookahead.
        const int chunkSamples = 2048;
        const float thresholdDb = -6f;
        const float releaseMs = 80f;
        const float lookA = 3f;
        const float lookB = MaxLookaheadMs;

        static float Gen(int ch, int i)
        {
            float tone = MathF.Sin(2f * MathF.PI * 523f * i / SampleRate);
            float spike = (i % 167 == 0) ? 3.0f : 0f;
            return (1.4f * tone + spike) * (ch == 0 ? 1.0f : 0.8f);
        }

        var dur1 = TimeSpan.FromSeconds((double)chunkSamples / SampleRate);

        // Deque node: chunk 1 @ lookA (builds deque for A), chunk 2 @ lookB (rebuild for B).
        using var dequeNode = CreateNode(thresholdDb: thresholdDb, releaseMs: releaseMs, lookaheadMs: lookA);
        // Scan oracle: animated step lookahead A -> B at the chunk boundary.
        using var scanNode = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(thresholdDb),
            Release = Property.CreateAnimatable(releaseMs),
            Lookahead = CreateAnimatedStep(lookA, lookB, dur1),
            MakeupGain = Property.CreateAnimatable(0f),
        };

        // Chunk 1 (both nodes @ lookA).
        using (var inDeque1 = CreateBuffer(2, chunkSamples, (ch, i) => Gen(ch, i)))
        using (var inScan1 = CreateBuffer(2, chunkSamples, (ch, i) => Gen(ch, i)))
        {
            dequeNode.AddInput(new StubInputNode(inDeque1));
            scanNode.AddInput(new StubInputNode(inScan1));
            using var _d = dequeNode.Process(CreateContext(chunkSamples, start: TimeSpan.Zero));
            using var _s = scanNode.Process(CreateContext(chunkSamples, start: TimeSpan.Zero));
        }

        // Switch the deque node's static lookahead to B; the scan node steps to B automatically.
        dequeNode.Lookahead.CurrentValue = lookB;

        using var inDeque2 = CreateBuffer(2, chunkSamples, (ch, i) => Gen(ch, chunkSamples + i));
        using var inScan2 = CreateBuffer(2, chunkSamples, (ch, i) => Gen(ch, chunkSamples + i));
        dequeNode.ClearInputs();
        dequeNode.AddInput(new StubInputNode(inDeque2));
        scanNode.ClearInputs();
        scanNode.AddInput(new StubInputNode(inScan2));

        using var outDeque = dequeNode.Process(CreateContext(chunkSamples, start: dur1));
        using var outScan = scanNode.Process(CreateContext(chunkSamples, start: dur1));

        for (int ch = 0; ch < 2; ch++)
        {
            var d = outDeque.GetChannelData(ch);
            var s = outScan.GetChannelData(ch);
            for (int i = 0; i < chunkSamples; i++)
            {
                Assert.That(d[i], Is.EqualTo(s[i]).Within(1e-6f),
                    $"rebuild deque vs scan mismatch ch {ch} sample {i}: deque={d[i]} scan={s[i]}.");
            }
        }
    }

    [Test]
    public void Process_TinyAboveThreshold_LimitsAndStaysFinite()
    {
        // Drives the limiting branch (windowPeak > thresholdLin) at a tiny magnitude just above the
        // minimum threshold (-60 dB -> ~0.001 linear), confirming the gain math stays finite and capped
        // for sub-mill scale signals (no denormal/precision blow-up).
        const int sampleCount = 2048;
        const float thresholdDb = MinThresholdDb; // -60 dB
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);  // ~0.001
        float amp = thresholdLin * 1.1f;                          // just above threshold

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => amp * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: thresholdDb, lookaheadMs: 0f);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True, $"ch {ch} sample {i} must be finite.");
                Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-6f),
                    $"ch {ch} sample {i} must be capped at the tiny threshold.");
            }
        }
    }

    [Test]
    public void Process_NonFiniteAnimatedParameters_AreClampedOnPerSamplePath()
    {
        // Mirrors the static non-finite-parameter test on the ANIMATED per-sample Derive path. All four
        // parameters are animated to non-finite values. Release is the load-bearing one: without
        // ClampFinite a NaN Release makes releaseCoef = Exp(NaN) = NaN, which poisons _currentGain
        // through the IIR and turns the output NaN — so this test genuinely fails if per-sample coercion
        // is removed (a Threshold/Lookahead-only variant would pass even uncoerced and prove nothing).
        const int sampleCount = 2048;

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = new LimiterNode
        {
            Threshold = CreateAnimatedConstant(float.NaN),
            Release = CreateAnimatedConstant(float.NaN),
            Lookahead = CreateAnimatedConstant(float.PositiveInfinity),
            MakeupGain = CreateAnimatedConstant(float.NaN),
        };
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"animated non-finite params ch {ch} sample {i} must be sanitized.");
            }
        }
    }

    [Test]
    public void Process_LookaheadChangeBeforeWindowFills_RebuildsFromPartialHistory()
    {
        // Exercises EnsureDeque's span-clamp branch (span = last+1 < lookahead+1): a static lookahead
        // change on a reused node whose total history is SHORTER than the new window. The first chunk is
        // only 64 samples, far below the MaxLookaheadMs window (~960 @48k), so the rebuild must clamp to
        // the available history instead of reading past the written peak ring. Compared against an
        // animated step-lookahead scan oracle fed the identical signal.
        const int chunk1 = 64;
        const int chunk2 = 2048;
        const float thresholdDb = -6f;
        const float releaseMs = 80f;
        const float lookA = 1f;
        const float lookB = MaxLookaheadMs;

        static float Gen(int ch, int i)
        {
            float tone = MathF.Sin(2f * MathF.PI * 523f * i / SampleRate);
            float spike = (i % 17 == 0) ? 3.0f : 0f;
            return (1.4f * tone + spike) * (ch == 0 ? 1.0f : 0.8f);
        }

        var dur1 = TimeSpan.FromSeconds((double)chunk1 / SampleRate);

        using var dequeNode = CreateNode(thresholdDb: thresholdDb, releaseMs: releaseMs, lookaheadMs: lookA);
        using var scanNode = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(thresholdDb),
            Release = Property.CreateAnimatable(releaseMs),
            Lookahead = CreateAnimatedStep(lookA, lookB, dur1),
            MakeupGain = Property.CreateAnimatable(0f),
        };

        // Chunk 1: tiny history at lookA.
        using (var inDeque1 = CreateBuffer(2, chunk1, (ch, i) => Gen(ch, i)))
        using (var inScan1 = CreateBuffer(2, chunk1, (ch, i) => Gen(ch, i)))
        {
            dequeNode.AddInput(new StubInputNode(inDeque1));
            scanNode.AddInput(new StubInputNode(inScan1));
            using var _d = dequeNode.Process(CreateContext(chunk1, start: TimeSpan.Zero));
            using var _s = scanNode.Process(CreateContext(chunk1, start: TimeSpan.Zero));
        }

        // Switch the deque node's static lookahead to the large window; the rebuild must clamp to the
        // 64-sample history rather than over-reading the peak ring.
        dequeNode.Lookahead.CurrentValue = lookB;

        using var inDeque2 = CreateBuffer(2, chunk2, (ch, i) => Gen(ch, chunk1 + i));
        using var inScan2 = CreateBuffer(2, chunk2, (ch, i) => Gen(ch, chunk1 + i));
        dequeNode.ClearInputs();
        dequeNode.AddInput(new StubInputNode(inDeque2));
        scanNode.ClearInputs();
        scanNode.AddInput(new StubInputNode(inScan2));

        using var outDeque = dequeNode.Process(CreateContext(chunk2, start: dur1));
        using var outScan = scanNode.Process(CreateContext(chunk2, start: dur1));

        for (int ch = 0; ch < 2; ch++)
        {
            var d = outDeque.GetChannelData(ch);
            var s = outScan.GetChannelData(ch);
            for (int i = 0; i < chunk2; i++)
            {
                Assert.That(d[i], Is.EqualTo(s[i]).Within(1e-6f),
                    $"partial-history rebuild mismatch ch {ch} sample {i}: deque={d[i]} scan={s[i]}.");
            }
        }
    }

    [Test]
    public void Process_StaticAfterAnimatedChunk_RebuildsDequeFromInvalidation()
    {
        // Pins ProcessAnimated's `_dequeLookahead = -1` invalidation: a static chunk that follows an
        // animated chunk on the SAME reused node must REBUILD the deque from the peak ring, not reuse the
        // stale deque left by an earlier static chunk (the animated chunk advances the peak ring but never
        // touches the deque). Sequence on one node: static(L) -> animated-constant(L) -> static(L), all
        // contiguous. Compared against a pure-static 3-chunk reference fed the identical stream; if the
        // invalidation were removed, the 3rd chunk would reuse a deque that never saw the 2nd chunk.
        const int chunkSamples = 2048;
        const float thresholdDb = -6f;
        const float releaseMs = 80f;
        const float lookMs = MaxLookaheadMs;

        static float Gen(int ch, int i)
        {
            float tone = MathF.Sin(2f * MathF.PI * 523f * i / SampleRate);
            float spike = (i % 211 == 0) ? 3.0f : 0f;
            return (1.4f * tone + spike) * (ch == 0 ? 1.0f : 0.85f);
        }

        using var node = CreateNode(thresholdDb: thresholdDb, releaseMs: releaseMs, lookaheadMs: lookMs);
        using var reference = CreateNode(thresholdDb: thresholdDb, releaseMs: releaseMs, lookaheadMs: lookMs);

        var constantAnim = new KeyFrameAnimation<float>();
        constantAnim.KeyFrames.Add(new KeyFrame<float> { Value = lookMs, KeyTime = TimeSpan.Zero });

        TimeSpan start = TimeSpan.Zero;
        for (int c = 0; c < 3; c++)
        {
            // Middle chunk runs the animated path on `node`, forcing _dequeLookahead = -1.
            node.Lookahead.Animation = c == 1 ? constantAnim : null;

            using var inNode = CreateBuffer(2, chunkSamples, (ch, i) => Gen(ch, c * chunkSamples + i));
            using var inRef = CreateBuffer(2, chunkSamples, (ch, i) => Gen(ch, c * chunkSamples + i));
            var ctx = CreateContext(chunkSamples, start: start);

            node.ClearInputs();
            node.AddInput(new StubInputNode(inNode));
            reference.ClearInputs();
            reference.AddInput(new StubInputNode(inRef));

            using var outNode = node.Process(ctx);
            using var outRef = reference.Process(CreateContext(chunkSamples, start: start));

            if (c == 2)
            {
                for (int ch = 0; ch < 2; ch++)
                {
                    var n = outNode.GetChannelData(ch);
                    var r = outRef.GetChannelData(ch);
                    for (int i = 0; i < chunkSamples; i++)
                    {
                        Assert.That(n[i], Is.EqualTo(r[i]).Within(1e-6f),
                            $"post-animated static chunk diverged from pure-static reference ch {ch} sample {i}: {n[i]} vs {r[i]}.");
                    }
                }
            }

            start += ctx.TimeRange.Duration;
        }
    }

    [TestCase(0f)]
    [TestCase(0.02f)]
    [TestCase(0.25f)]
    [TestCase(MaxLookaheadMs)]
    public void Process_StaticDeque_MatchesAnimatedScan_RandomizedSweep(float lookaheadMs)
    {
        // Property-style hardening of the deque==scan equivalence: a seeded PRNG (fixed seed for
        // determinism, no flakiness) generates a transient-rich stereo signal that is fed identically to
        // the static O(1) deque path and the animated O(lookahead) scan path across multiple contiguous
        // chunks. Sweeps lookahead 0, sub-sample, small, and max so the window-max relation is checked
        // far beyond the single hand-picked signal. Any divergence is a deque bug.
        const int chunkSamples = 1024;
        const int chunks = 4;
        const float thresholdDb = -6f;
        const float releaseMs = 60f;
        int total = chunks * chunkSamples;

        var rng = new Random(98765);
        float[,] sig = new float[2, total];
        for (int i = 0; i < total; i++)
        {
            float tone = MathF.Sin(2f * MathF.PI * 440f * i / SampleRate);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float transient = rng.NextDouble() < 0.03 ? 3.0f * (float)(rng.NextDouble() * 2.0 - 1.0) : 0f;
            sig[0, i] = 1.2f * tone + 0.3f * noise + transient;
            sig[1, i] = 0.8f * (1.2f * tone + 0.3f * noise) + 0.5f * transient;
        }

        using var staticNode = CreateNode(thresholdDb: thresholdDb, releaseMs: releaseMs, lookaheadMs: lookaheadMs);
        using var animatedNode = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(thresholdDb),
            Release = Property.CreateAnimatable(releaseMs),
            Lookahead = CreateAnimatedConstant(lookaheadMs),
            MakeupGain = Property.CreateAnimatable(0f),
        };

        TimeSpan start = TimeSpan.Zero;
        for (int c = 0; c < chunks; c++)
        {
            int baseIndex = c * chunkSamples;
            using var inStatic = CreateBuffer(2, chunkSamples, (ch, i) => sig[ch, baseIndex + i]);
            using var inAnimated = CreateBuffer(2, chunkSamples, (ch, i) => sig[ch, baseIndex + i]);
            var ctx = CreateContext(chunkSamples, start: start);

            staticNode.ClearInputs();
            staticNode.AddInput(new StubInputNode(inStatic));
            animatedNode.ClearInputs();
            animatedNode.AddInput(new StubInputNode(inAnimated));

            using var outStatic = staticNode.Process(ctx);
            using var outAnimated = animatedNode.Process(CreateContext(chunkSamples, start: start));

            for (int ch = 0; ch < 2; ch++)
            {
                var s = outStatic.GetChannelData(ch);
                var a = outAnimated.GetChannelData(ch);
                for (int i = 0; i < chunkSamples; i++)
                {
                    Assert.That(s[i], Is.EqualTo(a[i]).Within(1e-6f),
                        $"deque vs scan mismatch lookaheadMs={lookaheadMs} chunk {c} ch {ch} sample {i}: static={s[i]} animated={a[i]}.");
                }
            }

            start += ctx.TimeRange.Duration;
        }
    }

    [Test]
    public void Process_AnimatedLookahead_TakesEffect_DiffersFromFixedLookahead()
    {
        // The finite+bounded animated-lookahead test cannot fail on a lookahead-alignment regression
        // (limiting is conservative, so almost any delay stays finite and capped). This proves the
        // per-sample-varying delay is genuinely applied: a transient-rich signal run through a ramped
        // lookahead (0 -> max) must produce output that DIFFERS from both a fixed-0 and a fixed-max
        // lookahead run. If the animated lookahead were silently ignored, it would match one of them.
        const int sampleCount = 8192;
        const float thresholdDb = -3f;
        var duration = TimeSpan.FromSeconds((double)sampleCount / SampleRate);

        static float Gen(int ch, int i)
        {
            float tone = MathF.Sin(2f * MathF.PI * 330f * i / SampleRate);
            float spike = (i % 97 == 0) ? 3.0f : 0f;
            return 1.5f * tone + spike;
        }

        using var ramped = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(thresholdDb),
            Release = Property.CreateAnimatable(50f),
            Lookahead = CreateAnimatedRamp(0f, MaxLookaheadMs, duration),
            MakeupGain = Property.CreateAnimatable(0f),
        };
        using var fixed0 = CreateNode(thresholdDb: thresholdDb, releaseMs: 50f, lookaheadMs: 0f, makeupGainDb: 0f);
        using var fixedMax = CreateNode(thresholdDb: thresholdDb, releaseMs: 50f, lookaheadMs: MaxLookaheadMs, makeupGainDb: 0f);

        using var inRamp = CreateBuffer(1, sampleCount, Gen);
        using var in0 = CreateBuffer(1, sampleCount, Gen);
        using var inMax = CreateBuffer(1, sampleCount, Gen);
        ramped.AddInput(new StubInputNode(inRamp));
        fixed0.AddInput(new StubInputNode(in0));
        fixedMax.AddInput(new StubInputNode(inMax));

        using var outRamp = ramped.Process(CreateContext(sampleCount));
        using var out0 = fixed0.Process(CreateContext(sampleCount));
        using var outMax = fixedMax.Process(CreateContext(sampleCount));

        var r = outRamp.GetChannelData(0);
        var z = out0.GetChannelData(0);
        var m = outMax.GetChannelData(0);

        int diffFrom0 = 0, diffFromMax = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            if (MathF.Abs(r[i] - z[i]) > 1e-4f) diffFrom0++;
            if (MathF.Abs(r[i] - m[i]) > 1e-4f) diffFromMax++;
        }

        Assert.That(diffFrom0, Is.GreaterThan(sampleCount / 20),
            $"Ramped lookahead output is suspiciously close to fixed-0 ({diffFrom0} differing samples) — animation may be ignored.");
        Assert.That(diffFromMax, Is.GreaterThan(sampleCount / 20),
            $"Ramped lookahead output is suspiciously close to fixed-max ({diffFromMax} differing samples) — animation may be ignored.");
    }

    [Test]
    public void Process_ChannelGrowWithLookahead_NewChannelDelayLineIsClean()
    {
        // Strengthens channel-count-change coverage with an EXACT per-channel delay assertion under a
        // non-zero lookahead. After a mono->stereo grow (which reallocates the per-channel delay lines via
        // InitializeBuffers), an impulse on both channels of the grown buffer must re-emerge at exactly
        // lookaheadSamples on each channel — proving the channel-major indexing (ch*sampleCount+i) is
        // correct on the resized buffer and the newly-added channel's delay line starts clean (no leakage).
        const int sampleCount = 2048;
        const float lookaheadMs = 5f;
        int lookaheadSamples = LookaheadSamples(SampleRate, lookaheadMs);

        using var node = CreateNode(thresholdDb: 0f, lookaheadMs: lookaheadMs);

        // Chunk 1: mono, silent — establishes the node at one channel.
        using (var mono = CreateBuffer(1, sampleCount, (_, _) => 0f))
        {
            node.AddInput(new StubInputNode(mono));
            using var _ = node.Process(CreateContext(sampleCount, start: TimeSpan.Zero));
        }

        // Chunk 2: grow to stereo (contiguous) with an impulse at index 0 on both channels.
        var dur = TimeSpan.FromSeconds((double)sampleCount / SampleRate);
        using var stereo = CreateBuffer(2, sampleCount, (_, i) => i == 0 ? 0.5f : 0f);
        node.ClearInputs();
        node.AddInput(new StubInputNode(stereo));
        using var output = node.Process(CreateContext(sampleCount, start: dur));

        Assert.That(output.ChannelCount, Is.EqualTo(2));
        for (int ch = 0; ch < 2; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                float expected = i == lookaheadSamples ? 0.5f : 0f;
                Assert.That(data[i], Is.EqualTo(expected).Within(1e-6f),
                    $"grown channel {ch} sample {i}: expected {expected}, got {data[i]} (delay line not clean or mis-indexed).");
            }
        }
    }

    [Test]
    public void Process_DefaultLimiter_OutputStaysBelowMasterLimiterCeiling()
    {
        // The always-on master limiter (Composer -> AudioMath.ApplyLimiter at 1.0 linear) must not
        // re-limit a default-configured LimiterEffect's output: with Threshold -1 dB and 0 dB makeup the
        // capped peak is ~0.891 < 1.0, so the master leaves the buffer bit-identical. This pins the
        // no-double-limiting invariant that justifies the -1 dB default; a change to the master ceiling or
        // DefaultThresholdDb that reintroduced double-limiting would fail here.
        const int sampleCount = 8192;
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(
            thresholdDb: DefaultThresholdDb,
            releaseMs: DefaultReleaseMs,
            lookaheadMs: DefaultLookaheadMs,
            makeupGainDb: DefaultMakeupGainDb);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        float thresholdLin = MathF.Pow(10f, DefaultThresholdDb / 20f); // ~0.891
        for (int ch = 0; ch < 2; ch++)
        {
            float[] limited = output.GetChannelData(ch).ToArray();

            // Sanity: the limiter already keeps peaks at/below its own ceiling, which is < 1.0.
            foreach (float s in limited)
                Assert.That(MathF.Abs(s), Is.LessThanOrEqualTo(thresholdLin + 1e-3f));

            // Apply the same master limiter the Composer always applies after effects.
            float[] mastered = (float[])limited.Clone();
            AudioMath.ApplyLimiter(mastered, 1.0f, 10.0f);

            for (int i = 0; i < mastered.Length; i++)
            {
                Assert.That(mastered[i], Is.EqualTo(limited[i]),
                    $"master limiter altered a default-limited sample at ch {ch} index {i} " +
                    $"({limited[i]} -> {mastered[i]}); the default path must not double-limit.");
            }
        }
    }

    private static IEnumerable<TestCaseData> ParameterRanges()
    {
        yield return new TestCaseData(MinThresholdDb, DefaultThresholdDb, MaxThresholdDb).SetName("Threshold");
        yield return new TestCaseData(MinReleaseMs, DefaultReleaseMs, MaxReleaseMs).SetName("Release");
        yield return new TestCaseData(MinLookaheadMs, DefaultLookaheadMs, MaxLookaheadMs).SetName("Lookahead");
        yield return new TestCaseData(MinMakeupGainDb, DefaultMakeupGainDb, MaxMakeupGainDb).SetName("MakeupGain");
    }

    [TestCaseSource(nameof(ParameterRanges))]
    public void LimiterParameters_RangeIsConsistent(float min, float def, float max)
    {
        // Catches an edit that puts a default outside its range or inverts a min/max at CI time — the
        // [Range] attributes and LimiterNode's per-sample clamps both read these constants, so a desync
        // within the set would ship silently. Mirrors CompressorParameters_RangeIsConsistent.
        Assert.That(min, Is.LessThan(max), "Min must be strictly less than Max.");
        Assert.That(def, Is.InRange(min, max), "Default must lie within [Min, Max].");
    }
}
