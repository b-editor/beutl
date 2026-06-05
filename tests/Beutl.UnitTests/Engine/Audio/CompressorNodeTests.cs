using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class CompressorNodeTests
{
    private const int SampleRate = 48000;

    private sealed class StubSourceNode : AudioNode
    {
        public required AudioBuffer Buffer { get; init; }

        public override AudioBuffer Process(AudioProcessContext context)
        {
            var copy = new AudioBuffer(Buffer.SampleRate, Buffer.ChannelCount, Buffer.SampleCount);
            Buffer.CopyTo(copy);
            return copy;
        }
    }

    private static AudioBuffer CreateSineBuffer(float amplitude, float frequencyHz, int sampleCount, int channels = 2, int sampleRate = SampleRate)
    {
        var buffer = new AudioBuffer(sampleRate, channels, sampleCount);
        for (int ch = 0; ch < channels; ch++)
        {
            var data = buffer.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                data[i] = amplitude * MathF.Sin(2f * MathF.PI * frequencyHz * i / sampleRate);
            }
        }
        return buffer;
    }

    private static AudioBuffer CreateConstantBuffer(float amplitude, int sampleCount, int channels = 2)
    {
        var buffer = new AudioBuffer(SampleRate, channels, sampleCount);
        for (int ch = 0; ch < channels; ch++)
        {
            buffer.GetChannelData(ch).Fill(amplitude);
        }
        return buffer;
    }

    private static float PeakDb(AudioBuffer buffer, int startSample)
    {
        float peak = 0f;
        for (int ch = 0; ch < buffer.ChannelCount; ch++)
        {
            var data = buffer.GetChannelData(ch);
            for (int i = startSample; i < buffer.SampleCount; i++)
            {
                float a = MathF.Abs(data[i]);
                if (a > peak) peak = a;
            }
        }
        return peak > 0f ? 20f * MathF.Log10(peak) : -100f;
    }

    private static float ChannelPeakDb(AudioBuffer buffer, int channel, int startSample)
    {
        float peak = 0f;
        var data = buffer.GetChannelData(channel);
        for (int i = startSample; i < buffer.SampleCount; i++)
        {
            float a = MathF.Abs(data[i]);
            if (a > peak) peak = a;
        }
        return peak > 0f ? 20f * MathF.Log10(peak) : -100f;
    }

    private static float PeakDbInWindow(AudioBuffer buffer, int startSample, int width)
    {
        int end = Math.Min(buffer.SampleCount, startSample + width);
        float peak = 0f;
        for (int ch = 0; ch < buffer.ChannelCount; ch++)
        {
            var data = buffer.GetChannelData(ch);
            for (int i = startSample; i < end; i++)
            {
                float a = MathF.Abs(data[i]);
                if (a > peak) peak = a;
            }
        }
        return peak > 0f ? 20f * MathF.Log10(peak) : -100f;
    }

    private static CompressorNode CreateNode(
        float threshold = -20f,
        float ratio = 4f,
        float attack = 5f,
        float release = 50f,
        float knee = 0f,
        float makeup = 0f)
    {
        return new CompressorNode
        {
            Threshold = Property.CreateAnimatable(threshold),
            Ratio = Property.CreateAnimatable(ratio),
            Attack = Property.CreateAnimatable(attack),
            Release = Property.CreateAnimatable(release),
            Knee = Property.CreateAnimatable(knee),
            MakeupGain = Property.CreateAnimatable(makeup)
        };
    }

    private static AudioProcessContext CreateContext(TimeSpan start, TimeSpan duration, int sampleRate = SampleRate)
    {
        return new AudioProcessContext(
            new TimeRange(start, duration),
            sampleRate,
            new AnimationSampler(),
            null);
    }

    [Test]
    public void Process_SilenceInput_ProducesExactSilenceOutput()
    {
        // End-to-end "silence in → silence out" smoke test. Note: this does NOT specifically
        // isolate the `peak > 0f` guard, because RecoverEnvelopeIfNonFinite would mask the
        // non-finite envelope state produced when Log10(0) = -Infinity propagates through the
        // IIR formula `inputDb + coeff * (_envelopeDb - inputDb)` (NaN appears at the
        // (-∞) + coeff·(+∞) step). The gain calculation against a 0-amplitude sample still
        // yields exactly 0 either way. Genuinely isolating that guard would require log capture
        // or exposing internal state. What this test does catch: any future bug that injects DC,
        // noise, or non-zero offset into a silent stream (e.g., a stray makeup application that
        // mishandles the additive identity, or a sanitizer that fails open).
        const int sampleCount = SampleRate / 4;
        using var input = new AudioBuffer(SampleRate, 2, sampleCount);
        // Default-constructed AudioBuffer is zeroed, so no fill needed.
        var source = new StubSourceNode { Buffer = input };
        var node = CreateNode();
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25));
        using var output = node.Process(ctx);

        for (int ch = 0; ch < output.ChannelCount; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < output.SampleCount; i++)
            {
                Assert.That(data[i], Is.EqualTo(0f),
                    $"Silent input must produce exact-zero output, but [{ch}][{i}] = {data[i]}");
            }
        }
    }

    [Test]
    public void Process_AttackTimeConstant_EnvelopeReachesAbout63PercentAfterAttackMs()
    {
        // Step input drives the envelope from MinDb toward inputDb_max. After exactly attackMs
        // milliseconds, a one-pole IIR with time constant attackMs should have reached
        // ~(1 - 1/e) ≈ 63% of the way to its target. This is the contract the ComputeCoeff
        // formula encodes; a regression like dropping the ms→s conversion (timeMs * sampleRate
        // instead of timeMs * 0.001f * sampleRate) would leave the envelope still near -100 dB.
        const float attackMs = 50f;
        const int sampleCount = SampleRate; // 1 s
        const int stepAt = SampleRate / 10; // step at 100 ms; envelope sits at MinDb until then
        using var input = new AudioBuffer(SampleRate, 1, sampleCount);
        var data = input.GetChannelData(0);
        for (int i = stepAt; i < sampleCount; i++)
        {
            data[i] = 1f; // exactly 0 dB peak after the step
        }

        // Threshold = -50 dB is the load-bearing choice. With the bug, envelope stays near
        // -100 dB (below threshold) → gainReductionDb = 0 → output = input → reconstructed
        // envelope clamps to thresholdDb = -50 dB. Setting threshold ABOVE the buggy envelope
        // (at -50, far above -100) makes the buggy reconstruction 13.21 dB away from the target
        // (-36.79 dB), well outside the ±5 dB tolerance. A threshold of -40 or lower would put
        // the buggy reconstruction within tolerance and the test would falsely pass.
        // Knee=0 keeps the gain formula linear so we can back-solve the envelope value.
        const float thresholdDb = -50f;
        const float ratio = 4f;
        const float slope = 1f - 1f / ratio; // 0.75
        var node = CreateNode(threshold: thresholdDb, ratio: ratio, attack: attackMs, release: 100f, knee: 0f);
        node.AddInput(new StubSourceNode { Buffer = input });
        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));
        using var output = node.Process(ctx);

        // Sample exactly attackMs after the step. At that point, in dB-domain:
        //   envelopeDb(t) = inputDb_max - (inputDb_max - inputDb_initial) * exp(-t / attackMs)
        // With inputDb_max = 0 dB (peak 1.0) and inputDb_initial = MinDb = -100 dB:
        //   envelopeDb ≈ 0 - 100 * (1/e) ≈ -36.79 dB at t = attackMs.
        int probeIdx = stepAt + (int)(attackMs * 0.001f * SampleRate);
        // Reconstruct envelopeDb from the observed gain reduction:
        //   gainLinear = 10^(-gainReductionDb / 20), envelope = output / input.
        float gainLinear = MathF.Abs(output.GetChannelData(0)[probeIdx] / data[probeIdx]);
        float gainReductionDb = -20f * MathF.Log10(gainLinear);

        // Direct assertion: the expected gain reduction at t = attackMs is
        //   slope * (-36.79 - thresholdDb) = 0.75 * (-36.79 - (-50)) = 0.75 * 13.21 ≈ 9.91 dB.
        // Under the ms→s bug, envelope stays below threshold so gainReductionDb is 0 — the
        // tolerance below excludes that. This is the load-bearing assertion.
        Assert.That(gainReductionDb, Is.EqualTo(9.91f).Within(2f),
            $"At t = attackMs ({attackMs} ms), expected ≈9.91 dB reduction but got {gainReductionDb:F2} dB. " +
            $"Near 0 dB indicates ComputeCoeff lost its ms→s conversion.");

        // Secondary back-solve for human readability of the failure mode:
        //   gainReductionDb = slope * (envelopeDb - thresholdDb) for envelopeDb > thresholdDb.
        float reconstructedEnvelopeDb = thresholdDb + gainReductionDb / slope;
        Assert.That(reconstructedEnvelopeDb, Is.EqualTo(-36.79f).Within(5f),
            $"After attackMs={attackMs} ms, envelope should reach ~63% (≈-36.79 dB) but got {reconstructedEnvelopeDb:F2} dB");
    }

    [Test]
    public void Process_BelowThreshold_LeavesSignalUnchanged()
    {
        // Amplitude 0.05 ≈ -26 dB peak, well below the -20 dB threshold, so output should be a
        // bit-identical pass-through. We verify per-sample equality (not just peak) so that any
        // unexpected residual gain reduction is caught immediately.
        const int sampleCount = SampleRate / 2;
        using var input = CreateSineBuffer(0.05f, 1000f, sampleCount);
        var source = new StubSourceNode { Buffer = input };

        var node = CreateNode();
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5));

        using var output = node.Process(ctx);

        for (int ch = 0; ch < input.ChannelCount; ch++)
        {
            var inData = input.GetChannelData(ch);
            var outData = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(outData[i], Is.EqualTo(inData[i]).Within(1e-5f));
            }
        }
    }

    [Test]
    public void Process_AboveThreshold_AppliesExpectedGainReduction()
    {
        // Sine well above the threshold: the per-sample peak detector dips at every zero crossing
        // so the steady-state reduction is somewhat below the textbook ratio formula. Tolerance
        // is loose enough to absorb that envelope ripple but tight enough to catch a slope sign
        // flip or a missing makeup application.
        const int sampleCount = SampleRate;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        var source = new StubSourceNode { Buffer = input };

        var node = CreateNode();
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));

        using var output = node.Process(ctx);

        float steadyStartSample = SampleRate / 2;
        float outputPeakDb = PeakDb(output, (int)steadyStartSample);

        Assert.That(outputPeakDb, Is.EqualTo(-13.5f).Within(1.5f));
    }

    [Test]
    public void Process_MakeupGain_RaisesOutputAboveReducedLevel()
    {
        const int sampleCount = SampleRate;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        var source = new StubSourceNode { Buffer = input };

        var node = CreateNode(makeup: 6f);
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));

        using var output = node.Process(ctx);

        float steadyStartSample = SampleRate / 2;
        float outputPeakDb = PeakDb(output, (int)steadyStartSample);

        // Makeup gain should add directly on top of the compressed level.
        Assert.That(outputPeakDb, Is.EqualTo(-7.5f).Within(1.5f));
    }

    [Test]
    public void Process_RatioOne_PassesSignalThroughUnchanged()
    {
        const int sampleCount = SampleRate / 4;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        var source = new StubSourceNode { Buffer = input };

        var node = CreateNode(ratio: 1f);
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25));

        using var output = node.Process(ctx);

        for (int ch = 0; ch < input.ChannelCount; ch++)
        {
            var inData = input.GetChannelData(ch);
            var outData = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(outData[i], Is.EqualTo(inData[i]).Within(1e-6f));
            }
        }
    }

    [Test]
    public void Process_RatioBelowOne_ClampsToPassthrough()
    {
        // Animation/programmatic assignment can push ratio below 1; the node must clamp it to 1
        // (passthrough) rather than amplify above the threshold.
        const int sampleCount = SampleRate / 4;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        var source = new StubSourceNode { Buffer = input };

        var node = CreateNode(ratio: 0.5f);
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25));

        using var output = node.Process(ctx);

        float outputPeakDb = PeakDb(output, sampleCount / 2);
        float inputPeakDb = PeakDb(input, 0);
        Assert.That(outputPeakDb, Is.EqualTo(inputPeakDb).Within(0.5f));
    }

    [Test]
    public void Process_LinkedStereo_AppliesSameGainToBothChannels()
    {
        // L = 0.9 sine drives compression; R = 0.05 sine sits below the threshold and would not
        // compress on its own. Linked-stereo behaviour applies the L-derived gain reduction to R
        // as well, so R's output should be R_input_peak attenuated by the same amount as L.
        const int sampleCount = SampleRate;
        using var input = new AudioBuffer(SampleRate, 2, sampleCount);
        float leftInputPeakDb = 20f * MathF.Log10(0.9f);
        float rightInputPeakDb = 20f * MathF.Log10(0.05f);
        var lData = input.GetChannelData(0);
        var rData = input.GetChannelData(1);
        for (int i = 0; i < sampleCount; i++)
        {
            float t = 2f * MathF.PI * 1000f * i / SampleRate;
            lData[i] = 0.9f * MathF.Sin(t);
            rData[i] = 0.05f * MathF.Sin(t);
        }
        var source = new StubSourceNode { Buffer = input };

        var node = CreateNode();
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));

        using var output = node.Process(ctx);

        int steadyStart = SampleRate / 2;
        float leftPeakDb = ChannelPeakDb(output, 0, steadyStart);
        float rightPeakDb = ChannelPeakDb(output, 1, steadyStart);

        // L should be compressed to ~-13.5 dB (same as the steady-state test above).
        Assert.That(leftPeakDb, Is.EqualTo(-13.5f).Within(1.5f),
            "Sanity check: this test relies on L being compressed; if this fails the linked-gain expectation below is moot.");

        // The L-channel gain reduction (positive dB number) inferred from the measurement is
        // applied to the R channel by linked-stereo design, so R should land at the same dB
        // distance below its input peak.
        float leftGainReductionDb = leftInputPeakDb - leftPeakDb;
        float expectedRightDb = rightInputPeakDb - leftGainReductionDb;
        Assert.That(rightPeakDb, Is.EqualTo(expectedRightDb).Within(1.5f));
    }

    [Test]
    public void Process_EnvelopeStateContinuesAcrossChunks()
    {
        // A node warmed up by a previous loud chunk must NOT reset its envelope when the next
        // chunk continues directly in time. We verify this by comparing the first sample of the
        // second chunk against a fresh node processing the same loud input from scratch:
        // the warmed-up node is already in compression so its first sample is quieter, while
        // the fresh node still has to ramp through the attack phase.
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);
        var ctx1 = CreateContext(TimeSpan.Zero, chunkDuration);
        var ctx2 = CreateContext(chunkDuration, chunkDuration);

        var nodeContinuing = CreateNode(release: 1000f);
        using var warmupInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeContinuing.AddInput(new StubSourceNode { Buffer = warmupInput });
        using var warmup = nodeContinuing.Process(ctx1);
        nodeContinuing.ClearInputs();
        using var followInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeContinuing.AddInput(new StubSourceNode { Buffer = followInput });
        using var followOutput = nodeContinuing.Process(ctx2);

        var nodeFresh = CreateNode(release: 1000f);
        using var freshInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new StubSourceNode { Buffer = freshInput });
        using var freshOutput = nodeFresh.Process(ctx1);

        float continuingFirst = MathF.Abs(followOutput.GetChannelData(0)[0]);
        float freshFirst = MathF.Abs(freshOutput.GetChannelData(0)[0]);
        Assert.That(continuingFirst, Is.LessThan(freshFirst));
    }

    [Test]
    public void Process_NonContiguousTimeRange_ResetsEnvelope()
    {
        // First chunk drives compression; the second chunk starts at a non-contiguous time and
        // must therefore reset the envelope so it begins fresh from MinDb.
        const int chunkSamples = SampleRate / 10;
        using var loud = CreateConstantBuffer(0.9f, chunkSamples);

        var node = CreateNode(release: 1000f);
        node.AddInput(new StubSourceNode { Buffer = loud });
        var ctx1 = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(chunkSamples / (double)SampleRate));
        using var firstOutput = node.Process(ctx1);

        node.ClearInputs();
        using var loud2 = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new StubSourceNode { Buffer = loud2 });
        // Start time jumps forward (seek), breaking contiguity.
        var ctxSeek = CreateContext(TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(chunkSamples / (double)SampleRate));
        using var seekedOutput = node.Process(ctxSeek);

        var nodeFresh = CreateNode(release: 1000f);
        using var loud3 = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new StubSourceNode { Buffer = loud3 });
        using var freshOutput = nodeFresh.Process(
            CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(chunkSamples / (double)SampleRate)));

        // After a seek-style discontinuity, the envelope was reset, so the first sample should
        // match a fresh node's first sample (within float tolerance).
        float seekedFirst = MathF.Abs(seekedOutput.GetChannelData(0)[0]);
        float freshFirst = MathF.Abs(freshOutput.GetChannelData(0)[0]);
        Assert.That(seekedFirst, Is.EqualTo(freshFirst).Within(1e-4f));
    }

    [Test]
    public void Process_SampleRateChange_ResetsEnvelope()
    {
        const int chunkSamples = SampleRate / 10;
        using var loud = CreateConstantBuffer(0.9f, chunkSamples);

        var node = CreateNode(release: 1000f);
        node.AddInput(new StubSourceNode { Buffer = loud });
        var ctx48 = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(chunkSamples / (double)SampleRate));
        using var firstOutput = node.Process(ctx48);

        node.ClearInputs();
        const int altSampleRate = 44100;
        using var loud44 = CreateSineBuffer(0.9f, 1000f, altSampleRate / 10, 2, altSampleRate);
        node.AddInput(new StubSourceNode { Buffer = loud44 });
        // Time continues but sample rate changed → must reset envelope (and recompute coefficients
        // for the new rate).
        var ctx44 = new AudioProcessContext(
            new TimeRange(TimeSpan.FromSeconds(chunkSamples / (double)SampleRate), TimeSpan.FromSeconds(0.1)),
            altSampleRate,
            new AnimationSampler(),
            null);
        using var secondOutput = node.Process(ctx44);

        // After a sample-rate switch the envelope is reset, so the first sample must match a
        // fresh node running at the new rate.
        var nodeFresh = CreateNode(release: 1000f);
        using var freshInput = CreateSineBuffer(0.9f, 1000f, altSampleRate / 10, 2, altSampleRate);
        nodeFresh.AddInput(new StubSourceNode { Buffer = freshInput });
        var ctxFresh = new AudioProcessContext(
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(0.1)),
            altSampleRate,
            new AnimationSampler(),
            null);
        using var freshOutput = nodeFresh.Process(ctxFresh);

        float secondFirst = MathF.Abs(secondOutput.GetChannelData(0)[0]);
        float freshFirst = MathF.Abs(freshOutput.GetChannelData(0)[0]);
        Assert.That(secondFirst, Is.EqualTo(freshFirst).Within(1e-4f));
    }

    [Test]
    public void Reset_ClearsEnvelopeState()
    {
        // Process one chunk to drive the envelope into compression, then Reset() and process the
        // next chunk at a *contiguous* time. Without Reset(), the time-range check would NOT
        // trigger an automatic reset, so any difference from a fresh node must come from the
        // explicit Reset() call.
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);
        var ctx1 = CreateContext(TimeSpan.Zero, chunkDuration);
        var ctx2 = CreateContext(chunkDuration, chunkDuration);

        var node = CreateNode(release: 1000f);
        using var warmupInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new StubSourceNode { Buffer = warmupInput });
        using var firstOutput = node.Process(ctx1);

        node.Reset();
        node.ClearInputs();
        using var followInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new StubSourceNode { Buffer = followInput });
        using var afterResetOutput = node.Process(ctx2);

        var nodeFresh = CreateNode(release: 1000f);
        using var freshInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new StubSourceNode { Buffer = freshInput });
        using var freshOutput = nodeFresh.Process(ctx1);

        Assert.That(
            MathF.Abs(afterResetOutput.GetChannelData(0)[0]),
            Is.EqualTo(MathF.Abs(freshOutput.GetChannelData(0)[0])).Within(1e-4f));
    }

    [Test]
    public void Process_AnimatedThreshold_EngagesAnimatedPath()
    {
        // Threshold animates from -10 dB (no compression for 0.05 input) at t=0 to -40 dB
        // (heavy compression for 0.05 input) at t=0.5s. The output should be louder near t=0
        // and quieter near t=0.5s, proving the animated path is exercised.
        const int sampleCount = SampleRate / 2;
        using var input = CreateConstantBuffer(0.05f, sampleCount);
        var source = new StubSourceNode { Buffer = input };

        var thresholdAnim = new KeyFrameAnimation<float>();
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -10f, KeyTime = TimeSpan.Zero });
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -40f, KeyTime = TimeSpan.FromSeconds(0.5) });

        var thresholdProperty = Property.CreateAnimatable(-10f);
        thresholdProperty.Animation = thresholdAnim;

        var node = new CompressorNode
        {
            Threshold = thresholdProperty,
            Ratio = Property.CreateAnimatable(8f),
            Attack = Property.CreateAnimatable(1f),
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = Property.CreateAnimatable(0f)
        };
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5));

        using var output = node.Process(ctx);

        int lastQuarterStart = sampleCount * 3 / 4;
        float earlyPeakDb = PeakDb(output, 0);
        float latePeakDb = PeakDb(output, lastQuarterStart);

        // Early threshold sits above the input (no compression); late threshold sits below it
        // (compression engages). The late portion must therefore be measurably quieter.
        Assert.That(latePeakDb, Is.LessThan(earlyPeakDb - 2f),
            $"Animated threshold should attenuate the late portion (early≈{earlyPeakDb:F2} dB, late≈{latePeakDb:F2} dB)");
    }

    [Test]
    public void Process_InfinityInputSamples_RecoversAndDoesNotLeakNonFiniteOutput()
    {
        // First few samples on every channel are +Infinity, which (after MathF.Abs and Log10)
        // produces inputDb = +Infinity, polluting the envelope state. Subsequent samples are a
        // normal sine wave. The self-recovery clamp must reset the envelope and the output
        // sanitizer must ensure no NaN/Infinity sample escapes downstream.
        const int sampleCount = SampleRate / 4;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        for (int ch = 0; ch < input.ChannelCount; ch++)
        {
            var data = input.GetChannelData(ch);
            data[0] = float.PositiveInfinity;
            data[1] = float.PositiveInfinity;
        }

        var source = new StubSourceNode { Buffer = input };
        var node = CreateNode();
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25));
        using var output = node.Process(ctx);

        // No sample anywhere in the output may be NaN or Infinity.
        for (int ch = 0; ch < output.ChannelCount; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < output.SampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"Output sample [{ch}][{i}] = {data[i]} is not finite");
            }
        }

        // The steady-state region recovered to a sensible compressed level.
        float steadyPeakDb = PeakDb(output, sampleCount / 2);
        Assert.That(steadyPeakDb, Is.GreaterThan(-30f));
        Assert.That(steadyPeakDb, Is.LessThan(0f));
    }

    [Test]
    public void Process_NaNInputSamples_ProducesFiniteOutput()
    {
        // A NaN input sample multiplied by any gain stays NaN. The output sanitizer must
        // replace it with 0 so downstream consumers receive only finite samples.
        const int sampleCount = SampleRate / 4;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        input.GetChannelData(0)[0] = float.NaN;
        input.GetChannelData(1)[0] = float.NaN;

        var source = new StubSourceNode { Buffer = input };
        var node = CreateNode();
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25));
        using var output = node.Process(ctx);

        Assert.That(output.GetChannelData(0)[0], Is.EqualTo(0f));
        Assert.That(output.GetChannelData(1)[0], Is.EqualTo(0f));
        // Subsequent samples remain finite.
        for (int ch = 0; ch < output.ChannelCount; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 1; i < output.SampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True);
            }
        }
    }

    [Test]
    public void Process_SoftKnee_ProducesSmoothTransitionAroundThreshold()
    {
        // Soft knee starts attenuating before the input crosses the threshold; hard knee does
        // not. We feed a sine right at the threshold and verify that soft-knee output is lower
        // than hard-knee output, confirming the quadratic in-knee region engages.
        const int sampleCount = SampleRate / 2;
        // 0.1 amplitude → exactly -20 dB peak, matching the threshold.
        using var input = CreateSineBuffer(0.1f, 1000f, sampleCount);

        var hardKneeNode = CreateNode(threshold: -20f, ratio: 4f, knee: 0f);
        hardKneeNode.AddInput(new StubSourceNode { Buffer = input });
        using var hardOutput = hardKneeNode.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)));

        var softKneeNode = CreateNode(threshold: -20f, ratio: 4f, knee: 12f);
        softKneeNode.AddInput(new StubSourceNode { Buffer = input });
        using var softOutput = softKneeNode.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)));

        int steadyStart = sampleCount / 2;
        float hardPeakDb = PeakDb(hardOutput, steadyStart);
        float softPeakDb = PeakDb(softOutput, steadyStart);

        // Soft knee already engages before the input crosses the threshold, so its output peak
        // should be measurably lower than the hard-knee output.
        Assert.That(softPeakDb, Is.LessThan(hardPeakDb - 0.3f),
            $"Soft knee should attenuate near threshold (hard≈{hardPeakDb:F2} dB, soft≈{softPeakDb:F2} dB)");
    }

    [Test]
    public void Process_MonoBuffer_ProducesExpectedGainReduction()
    {
        // The implementation iterates over the channel count; a single-channel buffer must work
        // with no off-by-one and reach the same compression level as the stereo case.
        const int sampleCount = SampleRate;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount, channels: 1);
        var source = new StubSourceNode { Buffer = input };

        var node = CreateNode();
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));
        using var output = node.Process(ctx);

        Assert.That(output.ChannelCount, Is.EqualTo(1));
        float steadyPeakDb = PeakDb(output, sampleCount / 2);
        Assert.That(steadyPeakDb, Is.EqualTo(-13.5f).Within(1.5f));
    }

    [Test]
    public void Process_NoInputs_Throws()
    {
        var node = CreateNode();
        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.1));
        Assert.Throws<InvalidOperationException>(() => node.Process(ctx));
    }

    [Test]
    public void Process_AnimatedAttackRelease_ExercisesCoefficientCache()
    {
        // Animate Attack from 1 ms (fast) to 200 ms (slow) over the buffer. Every new ms value
        // invalidates the per-sample coefficient cache (`attackMs != lastAttackMs`), so
        // ComputeCoeff is recomputed repeatedly. To verify the recomputed coefficients actually
        // affect behaviour, we feed a step input (silence → loud) that arrives late in the
        // buffer when the slow attack value is in effect. Right after the step the envelope
        // hasn't clamped yet, so the transient peak must be louder than the eventually-settled
        // tail. With attack stuck at 1 ms throughout, the transient would clamp instantly and
        // this difference would not appear.
        const int sampleCount = SampleRate; // 1 s buffer
        int stepAt = SampleRate * 4 / 10;   // 400 ms in: animated attack ≈ 80 ms
        using var input = new AudioBuffer(SampleRate, 2, sampleCount);
        for (int ch = 0; ch < 2; ch++)
        {
            var data = input.GetChannelData(ch);
            for (int i = stepAt; i < sampleCount; i++)
            {
                data[i] = 0.9f * MathF.Sin(2f * MathF.PI * 1000f * i / SampleRate);
            }
        }

        var attackAnim = new KeyFrameAnimation<float>();
        attackAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 1f, KeyTime = TimeSpan.Zero });
        attackAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 200f, KeyTime = TimeSpan.FromSeconds(1.0) });
        var attackProperty = Property.CreateAnimatable(1f);
        attackProperty.Animation = attackAnim;

        var node = new CompressorNode
        {
            Threshold = Property.CreateAnimatable(-20f),
            Ratio = Property.CreateAnimatable(4f),
            Attack = attackProperty,
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = Property.CreateAnimatable(0f)
        };
        node.AddInput(new StubSourceNode { Buffer = input });

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));
        using var output = node.Process(ctx);

        for (int ch = 0; ch < output.ChannelCount; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < output.SampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True);
            }
        }

        int probeWidth = SampleRate / 200;          // 5 ms
        float transientPeakDb = PeakDbInWindow(output, stepAt, probeWidth);
        float settledPeakDb = PeakDbInWindow(output, sampleCount - probeWidth, probeWidth);

        Assert.That(transientPeakDb, Is.GreaterThan(settledPeakDb + 1.0f),
            $"Slow attack should leave a louder transient than the settled tail (transient≈{transientPeakDb:F2} dB, settled≈{settledPeakDb:F2} dB)");
    }

    [Test]
    public void Process_AnimatedPath_SmoothAcrossChunkBoundary()
    {
        // ProcessAnimated walks the input in fixed-size chunks. We send a buffer that straddles
        // several chunk boundaries and verify that the envelope state survives them: a steady
        // input must not show any visible discontinuity at sample indices that align with the
        // chunk size, which would indicate the envelope was reset at the boundary.
        const int chunkSize = 1024;
        const int sampleCount = chunkSize * 3 + 137; // straddles several boundaries
        using var input = CreateConstantBuffer(0.9f, sampleCount);

        // Animate threshold trivially so ProcessAnimated is taken; the value stays the same so
        // the gain reduction itself should be smooth.
        var thresholdAnim = new KeyFrameAnimation<float>();
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -20f, KeyTime = TimeSpan.Zero });
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -20f, KeyTime = TimeSpan.FromSeconds(sampleCount / (double)SampleRate) });
        var thresholdProperty = Property.CreateAnimatable(-20f);
        thresholdProperty.Animation = thresholdAnim;

        var node = new CompressorNode
        {
            Threshold = thresholdProperty,
            Ratio = Property.CreateAnimatable(4f),
            Attack = Property.CreateAnimatable(5f),
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = Property.CreateAnimatable(0f)
        };
        node.AddInput(new StubSourceNode { Buffer = input });

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate));
        using var output = node.Process(ctx);

        // Across each chunk boundary, the absolute change between adjacent samples must remain
        // bounded by the change inside the previous window — i.e. no sudden jump caused by
        // resetting state at a boundary.
        var data = output.GetChannelData(0);
        for (int boundary = chunkSize; boundary < sampleCount; boundary += chunkSize)
        {
            float prevDelta = MathF.Abs(data[boundary - 1] - data[boundary - 2]);
            float boundaryDelta = MathF.Abs(data[boundary] - data[boundary - 1]);
            // Tolerance allows for a tiny natural variation in the sine-like product but rejects
            // an envelope reset (which would create a step of order 0.1 or larger here).
            Assert.That(boundaryDelta, Is.LessThanOrEqualTo(prevDelta + 0.01f),
                $"Discontinuity at chunk boundary {boundary}: prevDelta={prevDelta:F6}, boundaryDelta={boundaryDelta:F6}");
        }
    }

    public enum AnimatedParam { Threshold, Ratio, Attack, Release, Knee, MakeupGain }

    [TestCase(AnimatedParam.Threshold)]
    [TestCase(AnimatedParam.Ratio)]
    [TestCase(AnimatedParam.Attack)]
    [TestCase(AnimatedParam.Release)]
    [TestCase(AnimatedParam.Knee)]
    [TestCase(AnimatedParam.MakeupGain)]
    public void Process_AnimatedNonFiniteValue_FallsBackWithoutMutingOutput(AnimatedParam param)
    {
        // A KeyFrame with NaN or Infinity on any animated parameter must not propagate to the
        // output sanitizer (which would silently mute the entire chunk). Instead each parameter
        // must fall back to its DefaultValue. We test every animated parameter so the
        // SafeParameter call cannot be silently dropped from any one of them.
        const int sampleCount = SampleRate / 4;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);

        var threshold = Property.CreateAnimatable(-20f);
        var ratio = Property.CreateAnimatable(4f);
        var attack = Property.CreateAnimatable(5f);
        var release = Property.CreateAnimatable(50f);
        var knee = Property.CreateAnimatable(0f);
        var makeup = Property.CreateAnimatable(0f);

        IProperty<float> target = param switch
        {
            AnimatedParam.Threshold => threshold,
            AnimatedParam.Ratio => ratio,
            AnimatedParam.Attack => attack,
            AnimatedParam.Release => release,
            AnimatedParam.Knee => knee,
            AnimatedParam.MakeupGain => makeup,
            _ => throw new ArgumentOutOfRangeException(nameof(param))
        };
        var anim = new KeyFrameAnimation<float>();
        anim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = float.NaN, KeyTime = TimeSpan.Zero });
        anim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = float.NaN, KeyTime = TimeSpan.FromSeconds(0.25) });
        // IProperty<float> exposes the Animation setter directly; no concrete-type cast needed.
        target.Animation = anim;

        var node = new CompressorNode
        {
            Threshold = threshold,
            Ratio = ratio,
            Attack = attack,
            Release = release,
            Knee = knee,
            MakeupGain = makeup
        };
        node.AddInput(new StubSourceNode { Buffer = input });

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25));
        using var output = node.Process(ctx);

        // If the NaN had reached the gain calc the output sanitizer would have zeroed every
        // sample and the peak would be -100 dB. Anything well above that proves the fallback
        // engaged for the parameter under test.
        float steadyPeakDb = PeakDb(output, sampleCount / 2);
        Assert.That(steadyPeakDb, Is.GreaterThan(-25f),
            $"Fallback failed for {param}: output appears to have been zeroed by NaN propagation");
    }

    [Test]
    public void Process_TooManyInputs_Throws()
    {
        const int sampleCount = SampleRate / 10;
        using var bufA = CreateConstantBuffer(0.1f, sampleCount);
        using var bufB = CreateConstantBuffer(0.1f, sampleCount);
        var node = CreateNode();
        node.AddInput(new StubSourceNode { Buffer = bufA });
        node.AddInput(new StubSourceNode { Buffer = bufB });
        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.1));
        Assert.Throws<InvalidOperationException>(() => node.Process(ctx));
    }

    [Test]
    public void Process_ZeroLengthInput_Static_ReturnsEmptyBuffer()
    {
        // A zero-length chunk (silent gap, end-of-stream tail) must not divide by zero, allocate
        // a stackalloc[0] for animation buffers, or otherwise misbehave. The static path is
        // exercised because no parameters are animated.
        using var input = new AudioBuffer(SampleRate, 2, 0);
        var node = CreateNode();
        node.AddInput(new StubSourceNode { Buffer = input });
        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.Zero);

        using var output = node.Process(ctx);

        Assert.That(output.SampleCount, Is.EqualTo(0));
        Assert.That(output.ChannelCount, Is.EqualTo(2));
        Assert.That(output.SampleRate, Is.EqualTo(SampleRate));
    }

    [Test]
    public void Process_ZeroLengthInput_Animated_ReturnsEmptyBuffer()
    {
        // Same as above, but with an animated parameter. The zero-length early-return in Process()
        // intercepts BOTH the static and animated paths before ProcessAnimated (and its
        // stackalloc float[bufferSize]) is ever entered, so this asserts the early-return contract
        // — not the animated chunk loop itself, which is unreachable for SampleCount == 0.
        using var input = new AudioBuffer(SampleRate, 2, 0);

        var thresholdAnim = new KeyFrameAnimation<float>();
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -20f, KeyTime = TimeSpan.Zero });
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -10f, KeyTime = TimeSpan.FromSeconds(1.0) });
        var thresholdProperty = Property.CreateAnimatable(-20f);
        thresholdProperty.Animation = thresholdAnim;

        var node = new CompressorNode
        {
            Threshold = thresholdProperty,
            Ratio = Property.CreateAnimatable(4f),
            Attack = Property.CreateAnimatable(5f),
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = Property.CreateAnimatable(0f)
        };
        node.AddInput(new StubSourceNode { Buffer = input });

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.Zero);
        using var output = node.Process(ctx);

        Assert.That(output.SampleCount, Is.EqualTo(0));
        Assert.That(output.ChannelCount, Is.EqualTo(2));
    }

    [Test]
    public void Process_AnimatedMakeupGain_AppliesPerSampleGain()
    {
        // Sweep MakeupGain from 0 dB (start) to +12 dB (end) over a steady loud signal. The
        // tail of the buffer must measure ~12 dB louder than the head — proving that the
        // animated `makeupDb - gainReductionDb` path actually mixes the per-sample makeup value
        // into the output (and that the sign is correct).
        const int sampleCount = SampleRate; // 1 s buffer
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);

        var makeupAnim = new KeyFrameAnimation<float>();
        makeupAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 0f, KeyTime = TimeSpan.Zero });
        makeupAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 12f, KeyTime = TimeSpan.FromSeconds(1.0) });
        var makeupProperty = Property.CreateAnimatable(0f);
        makeupProperty.Animation = makeupAnim;

        var node = new CompressorNode
        {
            Threshold = Property.CreateAnimatable(-20f),
            Ratio = Property.CreateAnimatable(4f),
            Attack = Property.CreateAnimatable(5f),
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = makeupProperty
        };
        node.AddInput(new StubSourceNode { Buffer = input });

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));
        using var output = node.Process(ctx);

        // Compare a steady-state window early in the buffer (makeup ≈ 0 dB) against one near
        // the end (makeup ≈ +12 dB). Both are after the attack ramp so envelope state is steady.
        int probeWidth = SampleRate / 100; // 10 ms
        float earlyPeakDb = PeakDbInWindow(output, SampleRate / 4, probeWidth);
        float latePeakDb = PeakDbInWindow(output, sampleCount - probeWidth, probeWidth);

        float observedRise = latePeakDb - earlyPeakDb;
        // 12 dB nominal; tolerance allows for ~3 dB of drift due to envelope ripple and the
        // 25%→100% sweep range covering 9 dB rather than the full 12 dB.
        Assert.That(observedRise, Is.GreaterThan(6f),
            $"Animated MakeupGain should raise output level (early≈{earlyPeakDb:F2} dB, late≈{latePeakDb:F2} dB, rise≈{observedRise:F2} dB)");
        Assert.That(observedRise, Is.LessThan(15f),
            $"Animated MakeupGain rise is implausibly large (early≈{earlyPeakDb:F2} dB, late≈{latePeakDb:F2} dB)");
    }

    [Test]
    public void Process_AnimatedRatio_BelowOne_ClampsToPassthrough()
    {
        // The animated path's clamp must mirror the static path: an animated ratio of 0.5
        // would otherwise produce slope = 1 - 1/0.5 = -1 which AMPLIFIES above the threshold.
        // After clamping to MinRatio=1, slope = 0 → passthrough.
        const int sampleCount = SampleRate / 4;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);

        var ratioAnim = new KeyFrameAnimation<float>();
        ratioAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 0.5f, KeyTime = TimeSpan.Zero });
        ratioAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 0.5f, KeyTime = TimeSpan.FromSeconds(0.25) });
        var ratioProperty = Property.CreateAnimatable(0.5f);
        ratioProperty.Animation = ratioAnim;

        var node = new CompressorNode
        {
            Threshold = Property.CreateAnimatable(-20f),
            Ratio = ratioProperty,
            Attack = Property.CreateAnimatable(5f),
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = Property.CreateAnimatable(0f)
        };
        node.AddInput(new StubSourceNode { Buffer = input });

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25));
        using var output = node.Process(ctx);

        float outputPeakDb = PeakDb(output, sampleCount / 2);
        float inputPeakDb = PeakDb(input, 0);
        // After clamp, output must NOT exceed input peak (no amplification).
        Assert.That(outputPeakDb, Is.LessThanOrEqualTo(inputPeakDb + 0.1f),
            $"Animated ratio<1 must clamp to passthrough; output {outputPeakDb:F2} dB exceeded input {inputPeakDb:F2} dB");
        // And it must roughly equal the input (no compression either).
        Assert.That(outputPeakDb, Is.EqualTo(inputPeakDb).Within(0.5f));
    }

    [Test]
    public void Process_AnimatedAttack_AboveMaxClampsAndKeepsEnvelopeMoving()
    {
        // An animated Attack of 1e9 ms would collapse the coefficient to exactly 1.0, freezing
        // the envelope so it never tracks the input — and therefore never crosses the threshold
        // and never compresses. After clamping to MaxAttackMs the coefficient is < 1.0 so the
        // envelope advances. We use a deep threshold (-50 dB) so the slow envelope reaches it
        // within a 1 s buffer; the visible output reduction is then proof the clamp engaged.
        const int sampleCount = SampleRate;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);

        var attackAnim = new KeyFrameAnimation<float>();
        attackAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 1e9f, KeyTime = TimeSpan.Zero });
        attackAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 1e9f, KeyTime = TimeSpan.FromSeconds(1.0) });
        var attackProperty = Property.CreateAnimatable(1e9f);
        attackProperty.Animation = attackAnim;

        var node = new CompressorNode
        {
            Threshold = Property.CreateAnimatable(-50f),
            Ratio = Property.CreateAnimatable(4f),
            Attack = attackProperty,
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = Property.CreateAnimatable(0f)
        };
        node.AddInput(new StubSourceNode { Buffer = input });

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));
        using var output = node.Process(ctx);

        // Probe in the last 50 ms window so the clamped 500 ms attack has had ~2 time constants
        // to bring the envelope above -50 dB and trigger compression.
        int probeWidth = SampleRate / 20;
        float steadyPeakDb = PeakDbInWindow(output, sampleCount - probeWidth, probeWidth);
        // Without clamping: envelope frozen at -100 dB → no compression → output ≈ input (-0.92 dB).
        // With clamping: envelope advances, crosses -50 dB threshold, triggers heavy reduction.
        Assert.That(steadyPeakDb, Is.LessThan(-10f),
            $"Animated Attack overshoot must be clamped so the envelope can still track; got {steadyPeakDb:F2} dB");
    }

    [Test]
    public void Process_MultipleAnimatedNonFiniteParameters_AllFallBackIndependently()
    {
        // Two animated parameters simultaneously produce NaN. With a single shared latch, only
        // one parameter would log and the second's fallback could be skipped if the latch were
        // also gating the substitution; the per-parameter HashSet ensures both still substitute
        // their fallbacks. Observable: output is not muted (would be -100 dB if NaN propagated).
        const int sampleCount = SampleRate / 4;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);

        var attackAnim = new KeyFrameAnimation<float>();
        attackAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = float.NaN, KeyTime = TimeSpan.Zero });
        attackAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = float.NaN, KeyTime = TimeSpan.FromSeconds(0.25) });
        var attackProperty = Property.CreateAnimatable(5f);
        attackProperty.Animation = attackAnim;

        var thresholdAnim = new KeyFrameAnimation<float>();
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = float.NaN, KeyTime = TimeSpan.Zero });
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = float.NaN, KeyTime = TimeSpan.FromSeconds(0.25) });
        var thresholdProperty = Property.CreateAnimatable(-20f);
        thresholdProperty.Animation = thresholdAnim;

        var node = new CompressorNode
        {
            Threshold = thresholdProperty,
            Ratio = Property.CreateAnimatable(4f),
            Attack = attackProperty,
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = Property.CreateAnimatable(0f)
        };
        node.AddInput(new StubSourceNode { Buffer = input });

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25));
        using var output = node.Process(ctx);

        for (int ch = 0; ch < output.ChannelCount; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < output.SampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"Output sample [{ch}][{i}] = {data[i]} is not finite");
            }
        }

        float steadyPeakDb = PeakDb(output, sampleCount / 2);
        Assert.That(steadyPeakDb, Is.GreaterThan(-25f),
            $"Both NaN parameters must fall back so output remains audible; got {steadyPeakDb:F2} dB");
    }

    [Test]
    public void Process_StaticAndAnimatedPaths_ProduceIdenticalOutputForConstantParameters()
    {
        // ProcessStatic and ProcessAnimated deliberately share ComputeCoeff / ComputeGainReductionDb
        // / ComputeGainLinear so the two paths cannot drift. This pins that parity directly: with a
        // constant-valued (two identical keyframes) Threshold animation, the animated path runs but
        // every sampled parameter equals what the static path reads, so the outputs must be
        // sample-for-sample identical. A coefficient-cache off-by-one, a chunk-boundary coefficient
        // reset, or a slope re-derivation bug that uniformly scaled the animated output would break
        // this even though Process_AnimatedPath_SmoothAcrossChunkBoundary (smoothness only) passes.
        const int sampleCount = SampleRate / 2; // straddles many 1024-sample chunks
        var duration = TimeSpan.FromSeconds(sampleCount / (double)SampleRate);
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);

        // Static reference path (knee>0 and makeup!=0 so both branches are exercised in both paths).
        var staticNode = CreateNode(threshold: -20f, ratio: 4f, attack: 5f, release: 50f, knee: 6f, makeup: 3f);
        staticNode.AddInput(new StubSourceNode { Buffer = input });
        using var staticOut = staticNode.Process(CreateContext(TimeSpan.Zero, duration));

        // Animated path forced on via a constant Threshold animation (-20 → -20). The remaining
        // parameters have no animation, so AnimationSampler fills each with the same CurrentValue
        // the static path reads.
        var thresholdAnim = new KeyFrameAnimation<float>();
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -20f, KeyTime = TimeSpan.Zero });
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -20f, KeyTime = duration });
        var thresholdProperty = Property.CreateAnimatable(-20f);
        thresholdProperty.Animation = thresholdAnim;

        var animatedNode = new CompressorNode
        {
            Threshold = thresholdProperty,
            Ratio = Property.CreateAnimatable(4f),
            Attack = Property.CreateAnimatable(5f),
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(6f),
            MakeupGain = Property.CreateAnimatable(3f)
        };
        animatedNode.AddInput(new StubSourceNode { Buffer = input });
        using var animatedOut = animatedNode.Process(CreateContext(TimeSpan.Zero, duration));

        for (int ch = 0; ch < staticOut.ChannelCount; ch++)
        {
            var s = staticOut.GetChannelData(ch);
            var a = animatedOut.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(a[i], Is.EqualTo(s[i]).Within(1e-4f),
                    $"Static and animated paths diverged at [{ch}][{i}]: static={s[i]}, animated={a[i]}");
            }
        }
    }

    [Test]
    public void Process_SoftKnee_InKneeReductionMatchesClosedForm()
    {
        // Validate the soft-knee quadratic numerically, not just "soft < hard". A DC signal whose
        // peak level equals the threshold drives the envelope to a known value (= thresholdDb), so
        // diff = 0, landing in the middle of the knee. The closed form there is
        //   GR = slope * x^2 / (2*knee)  with x = diff + halfKnee = halfKnee.
        // The hard-knee formula would give 0 dB at diff == 0, so this point distinctively exercises
        // the quadratic branch of CompressorNode.ComputeGainReductionDb.
        const int sampleCount = SampleRate / 2; // long enough for the envelope to settle to inputDb
        const float thresholdDb = -20f;
        const float ratio = 4f;
        const float kneeDb = 12f;
        float amplitude = MathF.Pow(10f, thresholdDb / 20f); // 0.1 → peak dB == threshold
        using var input = CreateConstantBuffer(amplitude, sampleCount);

        var node = CreateNode(threshold: thresholdDb, ratio: ratio, attack: 1f, release: 1f, knee: kneeDb, makeup: 0f);
        node.AddInput(new StubSourceNode { Buffer = input });
        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate)));

        // After settling, output[last] = amplitude * 10^(-GR/20). Recover GR and compare to the
        // closed-form value at diff = 0.
        float settled = MathF.Abs(output.GetChannelData(0)[sampleCount - 1]);
        float measuredGrDb = -20f * MathF.Log10(settled / amplitude);

        float slope = 1f - 1f / ratio;                            // 0.75
        float halfKnee = kneeDb * 0.5f;                           // 6
        float expectedGrDb = slope * halfKnee * halfKnee / (2f * kneeDb); // 1.125 dB

        Assert.That(measuredGrDb, Is.EqualTo(expectedGrDb).Within(0.1f),
            $"In-knee reduction at the threshold must match the quadratic closed form " +
            $"(expected {expectedGrDb:F3} dB, got {measuredGrDb:F3} dB)");
    }

    [Test]
    public void Process_ExpressionBackedParameter_RoutesToStaticPathAndIgnoresExpression()
    {
        // hasAnimation keys solely on Animation != null, so an expression-backed property
        // (Animation == null, HasExpression == true) routes to ProcessStatic, which reads
        // CurrentValue and does NOT evaluate the expression per-sample. This pins that documented
        // contract: the output must equal a fully-static node with the same CurrentValue even
        // though the expression evaluates to a different number. If a future change flips the gate
        // to treat HasExpression as live (the FIXME's eventual goal), this test forces a deliberate
        // update instead of silently changing rendered audio.
        const int sampleCount = SampleRate / 2;
        var duration = TimeSpan.FromSeconds(sampleCount / (double)SampleRate);
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);

        var staticNode = CreateNode(threshold: -20f);
        staticNode.AddInput(new StubSourceNode { Buffer = input });
        using var staticOut = staticNode.Process(CreateContext(TimeSpan.Zero, duration));

        // CurrentValue stays -20; the expression evaluates to -40 (which would compress harder).
        // Because the static path ignores the expression, -40 must NOT take effect.
        var thresholdProperty = Property.CreateAnimatable(-20f);
        thresholdProperty.Expression = new StringExpression<float>("-40");
        Assert.That(thresholdProperty.HasExpression, Is.True);
        Assert.That(thresholdProperty.Animation, Is.Null);

        var exprNode = new CompressorNode
        {
            Threshold = thresholdProperty,
            Ratio = Property.CreateAnimatable(4f),
            Attack = Property.CreateAnimatable(5f),
            Release = Property.CreateAnimatable(50f),
            Knee = Property.CreateAnimatable(0f),
            MakeupGain = Property.CreateAnimatable(0f)
        };
        exprNode.AddInput(new StubSourceNode { Buffer = input });
        using var exprOut = exprNode.Process(CreateContext(TimeSpan.Zero, duration));

        for (int ch = 0; ch < staticOut.ChannelCount; ch++)
        {
            var s = staticOut.GetChannelData(ch);
            var e = exprOut.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(e[i], Is.EqualTo(s[i]).Within(1e-6f),
                    $"Expression-backed parameter must route to the static path and ignore the expression; mismatch at [{ch}][{i}]");
            }
        }
    }
}
