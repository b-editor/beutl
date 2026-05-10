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
    private const int LookaheadMs = 5;

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
        float thresholdDb = -1f,
        float releaseMs = 50f,
        float lookaheadMs = LookaheadMs,
        float makeupGainDb = 0f)
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
        // Threshold = -1 dB ~= 0.891. Use peak 0.5 (~ -6 dB).
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode();
        node.AddInput(new StubInputNode(input));

        var ctx = CreateContext(sampleCount);
        using var output = node.Process(ctx);

        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);

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

        // Hot sine well above threshold (peak 2.0 ~= +6 dB).
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: thresholdDb);
        node.AddInput(new StubInputNode(input));

        var ctx = CreateContext(sampleCount);
        using var output = node.Process(ctx);

        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);

        // After the lookahead window has fully filled, every output sample must be
        // at or below threshold (modulo tiny float epsilon).
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

        // Signal stays well below threshold so only makeup gain is applied.
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 0.25f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        // Use a generous threshold so the limiter never engages.
        using var node = CreateNode(thresholdDb: 0f, makeupGainDb: makeupDb);
        node.AddInput(new StubInputNode(input));

        var ctx = CreateContext(sampleCount);
        using var output = node.Process(ctx);

        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);

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

        var ctx = CreateContext(sampleCount);
        using var output = node.Process(ctx);

        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);

        // The right channel should have been attenuated proportionally even though it
        // never crossed the threshold on its own.
        var inR = input.GetChannelData(1);
        var outR = output.GetChannelData(1);

        // Find a region with a large left peak to confirm the right channel was scaled.
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

        // And the left channel must still respect the threshold.
        var outL = output.GetChannelData(0);
        for (int i = lookaheadSamples; i < sampleCount; i++)
        {
            Assert.That(MathF.Abs(outL[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f));
        }
    }

    [Test]
    public void Process_NonContiguousChunks_ResetsState()
    {
        // First chunk: hot sine that drives the limiter into reduction.
        const int firstSampleCount = 1024;
        using var hotInput = CreateBuffer(2, firstSampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode();
        node.AddInput(new StubInputNode(hotInput));

        var firstCtx = CreateContext(firstSampleCount, start: TimeSpan.Zero);
        using (var firstOut = node.Process(firstCtx)) { /* prime the state */ }

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
        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);

        // Hot first chunk to fill the delay line.
        using var hotInput = CreateBuffer(2, chunkSamples,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(releaseMs: 5000f);
        node.AddInput(new StubInputNode(hotInput));

        var firstCtx = CreateContext(chunkSamples, start: TimeSpan.Zero);
        using (var _ = node.Process(firstCtx)) { /* fill delay line */ }

        // Second chunk: silence following directly after the first chunk (no time gap).
        var secondStart = firstCtx.TimeRange.Start + firstCtx.TimeRange.Duration;
        using var silence = CreateBuffer(2, chunkSamples, (_, _) => 0f);
        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new StubInputNode(silence));

        var secondCtx = CreateContext(chunkSamples, start: secondStart);
        using var secondOut = node.Process(secondCtx);

        // The first lookahead-window worth of samples in the second chunk reads the
        // tail of the first chunk out of the delay line. If Reset() had fired
        // erroneously the delay line would have been cleared and these samples would
        // all be zero — the non-zero leak proves continuity is preserved.
        bool foundLeak = false;
        for (int ch = 0; ch < 2 && !foundLeak; ch++)
        {
            var data = secondOut.GetChannelData(ch);
            for (int i = 0; i < lookaheadSamples; i++)
            {
                if (MathF.Abs(data[i]) > 1e-3f)
                {
                    foundLeak = true;
                    break;
                }
            }
        }

        Assert.That(foundLeak, Is.True,
            "Contiguous chunk should preserve delay-line state from the previous chunk.");
    }

    [Test]
    public void Process_ChannelCountChange_ReinitializesBuffers()
    {
        const int sampleCount = 1024;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var node = CreateNode(thresholdDb: thresholdDb);

        // Stereo first.
        using var stereoIn = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        node.AddInput(new StubInputNode(stereoIn));

        using (var stereoOut = node.Process(CreateContext(sampleCount, start: TimeSpan.Zero))) { /* prime */ }

        // Switch to mono input — buffers must be re-initialized for the new channel count.
        using var monoIn = CreateBuffer(1, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));
        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new StubInputNode(monoIn));

        using var monoOut = node.Process(CreateContext(sampleCount, start: TimeSpan.FromSeconds(1)));

        Assert.That(monoOut.ChannelCount, Is.EqualTo(1));
        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);
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

            var ctx = CreateContext(sampleCount, sampleRate: sr);
            using var output = node.Process(ctx);

            Assert.That(output.SampleCount, Is.EqualTo(sampleCount), $"SR={sr} sample count mismatch");
            Assert.That(output.SampleRate, Is.EqualTo(sr));

            int lookaheadSamples = (int)(LookaheadMs / 1000f * sr);
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
    public void Process_LookaheadZero_LimitsCorrectly()
    {
        const int sampleCount = 2048;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = CreateNode(thresholdDb: thresholdDb, lookaheadMs: 0f);
        node.AddInput(new StubInputNode(input));

        var ctx = CreateContext(sampleCount);
        using var output = node.Process(ctx);

        // With lookahead=0 every output sample must respect the threshold (no
        // negative-index reads, no off-by-one crash).
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
    public void Process_NegativePeak_TriggersLimiter()
    {
        const int sampleCount = 2048;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        // DC bias of -1.5 — only negative peaks exceed threshold. This catches a
        // half-wave-rectification bug where the limiter would only respond to
        // positive samples.
        using var input = CreateBuffer(2, sampleCount, (_, _) => -1.5f);

        using var node = CreateNode(thresholdDb: thresholdDb);
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);
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

        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);
        var data = output.GetChannelData(0);
        for (int i = lookaheadSamples; i < sampleCount; i++)
        {
            Assert.That(MathF.Abs(data[i]), Is.LessThanOrEqualTo(thresholdLin + 1e-4f));
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
            Lookahead = Property.CreateAnimatable((float)LookaheadMs),
            MakeupGain = Property.CreateAnimatable(0f),
        };
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        // Output past the lookahead window must stay at or below threshold even when
        // the property is sourced from an animated path (per-sample buffer sampling).
        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);
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
    public void Process_LongAnimatedChunk_ProcessesAllSamples()
    {
        // SampleCount > the internal stackalloc chunk size to exercise the inner while loop.
        const int sampleCount = 5000;
        const float thresholdDb = -1f;
        float thresholdLin = MathF.Pow(10f, thresholdDb / 20f);

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        using var node = new LimiterNode
        {
            Threshold = CreateAnimatedConstant(thresholdDb),
            Release = Property.CreateAnimatable(50f),
            Lookahead = Property.CreateAnimatable((float)LookaheadMs),
            MakeupGain = Property.CreateAnimatable(0f),
        };
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        Assert.That(output.SampleCount, Is.EqualTo(sampleCount));

        int lookaheadSamples = (int)(LookaheadMs / 1000f * SampleRate);
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
    public void Process_NoInputs_Throws()
    {
        using var node = CreateNode();
        var ex = Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(128)));
        Assert.That(ex!.Message, Does.Contain("0"));
    }

    [Test]
    public void Process_MultipleInputs_Throws()
    {
        using var input1 = CreateBuffer(2, 128, (_, _) => 0f);
        using var input2 = CreateBuffer(2, 128, (_, _) => 0f);

        using var node = CreateNode();
        node.AddInput(new StubInputNode(input1));
        node.AddInput(new StubInputNode(input2));

        var ex = Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(128)));
        Assert.That(ex!.Message, Does.Contain("2"));
    }

    [Test]
    public void Process_NullUpstream_Throws()
    {
        using var node = CreateNode();
        node.AddInput(new NullInputNode());

        var ex = Assert.Throws<InvalidOperationException>(() => node.Process(CreateContext(128)));
        Assert.That(ex!.Message, Does.Contain("null"));
    }

    [Test]
    public void Process_SampleRateMismatch_Throws()
    {
        using var input = CreateBuffer(2, 128, (_, _) => 0f, sampleRate: 44100);
        using var node = CreateNode();
        node.AddInput(new StubInputNode(input));

        // Context says 48000, input is 44100 — must surface as a clear error rather than
        // silently producing wrong-pitch output.
        var ex = Assert.Throws<InvalidOperationException>(
            () => node.Process(CreateContext(128, sampleRate: SampleRate)));
        Assert.That(ex!.Message, Does.Contain("sample rate"));
    }

    [Test]
    public void Process_NaNParameter_DoesNotProduceNaNOutput()
    {
        const int sampleCount = 1024;
        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        // All four parameters set to NaN. Math.Clamp propagates NaN, so without the
        // float.IsFinite guard each parameter would corrupt _currentGain and the output
        // would become NaN samples that flow downstream into the audio device.
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
        const float thresholdLin = 1f; // 0 dB ceiling after clamp.

        using var input = CreateBuffer(2, sampleCount,
            (_, i) => 2.0f * MathF.Sin(2f * MathF.PI * 440f * i / SampleRate));

        // Way out of [Range(...)] bounds — must be clamped silently, not raise.
        using var node = new LimiterNode
        {
            Threshold = Property.CreateAnimatable(999f),
            Release = Property.CreateAnimatable(-100f),
            Lookahead = Property.CreateAnimatable(9999f),
            MakeupGain = Property.CreateAnimatable(999f),
        };
        node.AddInput(new StubInputNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        // Output must be finite. The threshold is clamped to MaxThresholdDb (0 dB) and
        // makeup to MaxMakeupGainDb, so the upper bound on the output magnitude is
        // thresholdLin * 10^(MaxMakeupGainDb / 20).
        float upperBound = thresholdLin * MathF.Pow(10f, LimiterEffect.MaxMakeupGainDb / 20f);
        int lookaheadSamples = (int)(LimiterEffect.MaxLookaheadMs / 1000f * SampleRate);
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
        const int sampleCount = 1024;

        // Sprinkle NaN and Infinity into the input. Without input sanitization NaN
        // would be written into the delay line and propagate to the output, while
        // Infinity would force currentPeak to Inf and produce Inf*0 = NaN once the
        // gain reduction kicks in.
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
}
