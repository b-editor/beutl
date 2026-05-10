using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

public class LimiterNodeTests
{
    private const int SampleRate = 48000;
    private const float LookaheadMs = LimiterEffect.DefaultLookaheadMs;

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
        float thresholdDb = LimiterEffect.DefaultThresholdDb,
        float releaseMs = LimiterEffect.DefaultReleaseMs,
        float lookaheadMs = LimiterEffect.DefaultLookaheadMs,
        float makeupGainDb = LimiterEffect.DefaultMakeupGainDb)
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

    [Test]
    public void Process_DifferentSampleRates_BehaveConsistently()
    {
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        foreach (int sr in new[] { 44100, 48000, 96000 })
        {
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
    }

    [Test]
    public void Process_SampleRateChange_OnSameNode_ReinitializesBuffers()
    {
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var node = CreateNode(thresholdDb: thresholdDb, lookaheadMs: LimiterEffect.MaxLookaheadMs);

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
        int lookaheadSamples = LookaheadSamples(96000, LimiterEffect.MaxLookaheadMs);
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

        using var node = CreateNode(thresholdDb: thresholdDb, lookaheadMs: LimiterEffect.MaxLookaheadMs);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        // Off-by-one in the lookahead clamp would collapse Read(_maxLookaheadSamples) to 0
        // (silence) for every sample. Assert non-zero output past the lookahead window.
        int lookaheadSamples = LookaheadSamples(SampleRate, LimiterEffect.MaxLookaheadMs);
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

    [Test]
    public void Process_LookaheadDelay_IsAccurate()
    {
        // Impulse at index 0 of an otherwise silent buffer — the impulse should re-emerge at
        // exactly index `lookaheadSamples` in the output.
        foreach (float lookaheadMs in new[] { 1f, 5f, 10f, LimiterEffect.MaxLookaheadMs })
        {
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

            Assert.That(peakIndex, Is.EqualTo(expectedDelay).Within(1),
                $"Impulse delay mismatch at lookaheadMs={lookaheadMs}: got {peakIndex}, expected {expectedDelay}.");
            Assert.That(peakValue, Is.GreaterThan(0.1f),
                $"Impulse was attenuated below recoverable threshold at lookaheadMs={lookaheadMs}.");
        }
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

        float upperBound = thresholdLin * MathF.Pow(10f, LimiterEffect.MaxMakeupGainDb / 20f);
        int lookaheadSamples = LookaheadSamples(SampleRate, LimiterEffect.MaxLookaheadMs);
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
        // LimiterNode.ProcessSingleSample.
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

        Assert.That(effect.Threshold.CurrentValue, Is.EqualTo(LimiterEffect.DefaultThresholdDb));
        Assert.That(effect.Release.CurrentValue, Is.EqualTo(LimiterEffect.DefaultReleaseMs));
        Assert.That(effect.Lookahead.CurrentValue, Is.EqualTo(LimiterEffect.DefaultLookaheadMs));
        Assert.That(effect.MakeupGain.CurrentValue, Is.EqualTo(LimiterEffect.DefaultMakeupGainDb));
    }
}
