using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Media;

using static Beutl.UnitTests.Engine.Audio.AudioTestBuffers;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class CompressorNodeTests
{
    private const int SampleRate = 48000;

    // Sine buffer with a +Infinity head sample on every channel: drives the envelope and product
    // non-finite, emitting the one-shot non-finite-sample warning once per armed period.
    private static AudioBuffer MakeInfinityHeadBuffer(int sampleCount, int sampleRate = SampleRate)
    {
        var buffer = CreateSineBuffer(0.9f, 1000f, sampleCount, 2, sampleRate);
        for (int ch = 0; ch < buffer.ChannelCount; ch++)
        {
            buffer.GetChannelData(ch)[0] = float.PositiveInfinity;
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
        // End-to-end "silence in → silence out" smoke test. It does NOT isolate the `peak > 0f`
        // guard (RecoverEnvelopeIfNonFinite masks the Log10(0) = -∞ envelope state, and the gain
        // calc yields 0 either way), but it catches any future bug injecting DC/noise/offset into
        // a silent stream (a stray makeup application, a sanitizer that fails open, etc.).
        const int sampleCount = SampleRate / 4;
        using var input = new AudioBuffer(SampleRate, 2, sampleCount);
        // Default-constructed AudioBuffer is zeroed, so no fill needed.
        var source = new BufferReplayNode(input);
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
        // A one-pole IIR with time constant attackMs should reach ~(1 - 1/e) ≈ 63% of its target
        // after attackMs. Catches a dropped ms→s conversion in ComputeCoeff, which would leave the
        // envelope near -100 dB.
        const float attackMs = 50f;
        const int sampleCount = SampleRate; // 1 s
        const int stepAt = SampleRate / 10; // step at 100 ms; envelope sits at MinDb until then
        using var input = new AudioBuffer(SampleRate, 1, sampleCount);
        var data = input.GetChannelData(0);
        for (int i = stepAt; i < sampleCount; i++)
        {
            data[i] = 1f; // exactly 0 dB peak after the step
        }

        // Threshold = -50 dB is load-bearing: it sits far above the buggy frozen envelope (-100 dB),
        // so the buggy reconstruction lands ~13 dB from the -36.79 dB target, outside the ±5 dB
        // tolerance (a threshold of -40 or lower would falsely pass). Knee=0 keeps the formula linear
        // so the envelope is back-solvable.
        const float thresholdDb = -50f;
        const float ratio = 4f;
        const float slope = 1f - 1f / ratio; // 0.75
        var node = CreateNode(threshold: thresholdDb, ratio: ratio, attack: attackMs, release: 100f, knee: 0f);
        node.AddInput(new BufferReplayNode(input));
        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));
        using var output = node.Process(ctx);

        // Sample exactly attackMs after the step: envelopeDb ≈ 0 - 100/e ≈ -36.79 dB there
        // (inputDb_max = 0 dB, initial = MinDb = -100 dB).
        int probeIdx = stepAt + (int)(attackMs * 0.001f * SampleRate);
        // Reconstruct envelopeDb from observed gain: gainLinear = output / input.
        float gainLinear = MathF.Abs(output.GetChannelData(0)[probeIdx] / data[probeIdx]);
        float gainReductionDb = -20f * MathF.Log10(gainLinear);

        // Load-bearing assertion: expected reduction ≈ slope * (-36.79 - thresholdDb) ≈ 9.91 dB.
        // Under the ms→s bug the envelope stays below threshold (reduction 0), which the tolerance excludes.
        Assert.That(gainReductionDb, Is.EqualTo(9.91f).Within(2f),
            $"At t = attackMs ({attackMs} ms), expected ≈9.91 dB reduction but got {gainReductionDb:F2} dB. " +
            $"Near 0 dB indicates ComputeCoeff lost its ms→s conversion.");

        // Secondary back-solve to make the failure mode human-readable.
        float reconstructedEnvelopeDb = thresholdDb + gainReductionDb / slope;
        Assert.That(reconstructedEnvelopeDb, Is.EqualTo(-36.79f).Within(5f),
            $"After attackMs={attackMs} ms, envelope should reach ~63% (≈-36.79 dB) but got {reconstructedEnvelopeDb:F2} dB");
    }

    [Test]
    public void Process_BelowThreshold_LeavesSignalUnchanged()
    {
        // Amplitude 0.05 (≈-26 dB) is below the -20 dB threshold, so output must be a bit-identical
        // pass-through (gain = 1.0 exactly). Asserted with no tolerance to catch any near-zero
        // residual gain a future bug might inject.
        const int sampleCount = SampleRate / 2;
        using var input = CreateSineBuffer(0.05f, 1000f, sampleCount);
        var source = new BufferReplayNode(input);

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
                Assert.That(outData[i], Is.EqualTo(inData[i]));
            }
        }
    }

    [Test]
    public void Process_AboveThreshold_AppliesExpectedGainReduction()
    {
        // Sine above threshold. Analytic steady-state is -15.23 dB but the per-sample peak detector
        // dips at zero crossings, so the measured peak sits near -13.5 dB. The ±2.5 band spans both
        // values: sensitive to a slope sign flip or missing makeup without encoding a detector-specific
        // magic number.
        const int sampleCount = SampleRate;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        var source = new BufferReplayNode(input);

        var node = CreateNode();
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));

        using var output = node.Process(ctx);

        float steadyStartSample = SampleRate / 2;
        float outputPeakDb = PeakDb(output, (int)steadyStartSample);

        Assert.That(outputPeakDb, Is.EqualTo(-13.5f).Within(2.5f));
    }

    [Test]
    public void Process_MakeupGain_RaisesOutputAboveReducedLevel()
    {
        const int sampleCount = SampleRate;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        var source = new BufferReplayNode(input);

        var node = CreateNode(makeup: 6f);
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));

        using var output = node.Process(ctx);

        float steadyStartSample = SampleRate / 2;
        float outputPeakDb = PeakDb(output, (int)steadyStartSample);

        // Makeup adds on top of the compressed level: same ±2.5 band shifted up by +6 dB
        // (measured near -7.5 dB).
        Assert.That(outputPeakDb, Is.EqualTo(-7.5f).Within(2.5f));
    }

    [Test]
    public void Process_RatioOne_PassesSignalThroughUnchanged()
    {
        const int sampleCount = SampleRate / 4;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        var source = new BufferReplayNode(input);

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
        var source = new BufferReplayNode(input);

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
        // L (0.9 sine) drives compression; R (0.05 sine) is below threshold and wouldn't compress
        // alone. Linked-stereo applies L's gain reduction to R, so R is attenuated by the same amount.
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
        var source = new BufferReplayNode(input);

        var node = CreateNode();
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));

        using var output = node.Process(ctx);

        int steadyStart = SampleRate / 2;
        float leftPeakDb = ChannelPeakDb(output, 0, steadyStart);
        float rightPeakDb = ChannelPeakDb(output, 1, steadyStart);

        // L should be compressed to ~-13.5 dB (same ±2.5 band as the steady-state test above).
        Assert.That(leftPeakDb, Is.EqualTo(-13.5f).Within(2.5f),
            "Sanity check: this test relies on L being compressed; if this fails the linked-gain expectation below is moot.");

        // L's measured gain reduction is applied to R by linked-stereo design, so R lands the same
        // dB distance below its input peak.
        float leftGainReductionDb = leftInputPeakDb - leftPeakDb;
        float expectedRightDb = rightInputPeakDb - leftGainReductionDb;
        Assert.That(rightPeakDb, Is.EqualTo(expectedRightDb).Within(1.5f));
    }

    [Test]
    public void Process_EnvelopeStateContinuesAcrossChunks()
    {
        // A node warmed up by a previous loud chunk must NOT reset its envelope on a time-contiguous
        // next chunk. The warmed-up node is already in compression, so its first sample is quieter
        // than a fresh node still ramping through attack.
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);
        var ctx1 = CreateContext(TimeSpan.Zero, chunkDuration);
        var ctx2 = CreateContext(chunkDuration, chunkDuration);

        var nodeContinuing = CreateNode(release: 1000f);
        using var warmupInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeContinuing.AddInput(new BufferReplayNode(warmupInput));
        using var warmup = nodeContinuing.Process(ctx1);
        nodeContinuing.ClearInputs();
        using var followInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeContinuing.AddInput(new BufferReplayNode(followInput));
        using var followOutput = nodeContinuing.Process(ctx2);

        var nodeFresh = CreateNode(release: 1000f);
        using var freshInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new BufferReplayNode(freshInput));
        using var freshOutput = nodeFresh.Process(ctx1);

        float continuingFirst = MathF.Abs(followOutput.GetChannelData(0)[0]);
        float freshFirst = MathF.Abs(freshOutput.GetChannelData(0)[0]);
        Assert.That(continuingFirst, Is.LessThan(freshFirst));
    }

    [Test]
    public void Process_NonContiguousTimeRange_ResetsEnvelope()
    {
        // Second chunk starts at a non-contiguous time, so the envelope must reset to MinDb.
        const int chunkSamples = SampleRate / 10;
        using var loud = CreateConstantBuffer(0.9f, chunkSamples);

        var node = CreateNode(release: 1000f);
        node.AddInput(new BufferReplayNode(loud));
        var ctx1 = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(chunkSamples / (double)SampleRate));
        using var firstOutput = node.Process(ctx1);

        node.ClearInputs();
        using var loud2 = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(loud2));
        // Start time jumps forward (seek), breaking contiguity.
        var ctxSeek = CreateContext(TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(chunkSamples / (double)SampleRate));
        using var seekedOutput = node.Process(ctxSeek);

        var nodeFresh = CreateNode(release: 1000f);
        using var loud3 = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new BufferReplayNode(loud3));
        using var freshOutput = nodeFresh.Process(
            CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(chunkSamples / (double)SampleRate)));

        // After the seek reset, the first sample should match a fresh node's first sample.
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
        node.AddInput(new BufferReplayNode(loud));
        var ctx48 = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(chunkSamples / (double)SampleRate));
        using var firstOutput = node.Process(ctx48);

        node.ClearInputs();
        const int altSampleRate = 44100;
        using var loud44 = CreateSineBuffer(0.9f, 1000f, altSampleRate / 10, 2, altSampleRate);
        node.AddInput(new BufferReplayNode(loud44));
        // Time continues but the sample rate changed → reset envelope and recompute coefficients.
        var ctx44 = new AudioProcessContext(
            new TimeRange(TimeSpan.FromSeconds(chunkSamples / (double)SampleRate), TimeSpan.FromSeconds(0.1)),
            altSampleRate,
            new AnimationSampler(),
            null);
        using var secondOutput = node.Process(ctx44);

        // After the sample-rate reset, the first sample must match a fresh node at the new rate.
        var nodeFresh = CreateNode(release: 1000f);
        using var freshInput = CreateSineBuffer(0.9f, 1000f, altSampleRate / 10, 2, altSampleRate);
        nodeFresh.AddInput(new BufferReplayNode(freshInput));
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
        // Drive the envelope into compression, then Reset() and process a *contiguous* next chunk.
        // Contiguous time means no automatic reset, so any match with a fresh node must come from
        // the explicit Reset().
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);
        var ctx1 = CreateContext(TimeSpan.Zero, chunkDuration);
        var ctx2 = CreateContext(chunkDuration, chunkDuration);

        var node = CreateNode(release: 1000f);
        using var warmupInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(warmupInput));
        using var firstOutput = node.Process(ctx1);

        node.Reset();
        node.ClearInputs();
        using var followInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(followInput));
        using var afterResetOutput = node.Process(ctx2);

        var nodeFresh = CreateNode(release: 1000f);
        using var freshInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new BufferReplayNode(freshInput));
        using var freshOutput = nodeFresh.Process(ctx1);

        Assert.That(
            MathF.Abs(afterResetOutput.GetChannelData(0)[0]),
            Is.EqualTo(MathF.Abs(freshOutput.GetChannelData(0)[0])).Within(1e-4f));
    }

    [Test]
    public void Process_AnimatedThreshold_EngagesAnimatedPath()
    {
        // Threshold animates from -10 dB (no compression for 0.05 input) to -40 dB (heavy
        // compression), so the output goes from louder to quieter — proving the animated path runs.
        const int sampleCount = SampleRate / 2;
        using var input = CreateConstantBuffer(0.05f, sampleCount);
        var source = new BufferReplayNode(input);

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

        // Early threshold is above the input (no compression), late is below it (compression
        // engages), so the late portion must be measurably quieter.
        Assert.That(latePeakDb, Is.LessThan(earlyPeakDb - 2f),
            $"Animated threshold should attenuate the late portion (early≈{earlyPeakDb:F2} dB, late≈{latePeakDb:F2} dB)");
    }

    [Test]
    public void Process_InfinityInputSamples_RecoversAndDoesNotLeakNonFiniteOutput()
    {
        // The first samples on every channel are +Infinity, polluting the envelope; the rest is a
        // normal sine. The self-recovery clamp must reset the envelope and the sanitizer must keep
        // any NaN/Infinity from escaping downstream.
        const int sampleCount = SampleRate / 4;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        for (int ch = 0; ch < input.ChannelCount; ch++)
        {
            var data = input.GetChannelData(ch);
            data[0] = float.PositiveInfinity;
            data[1] = float.PositiveInfinity;
        }

        var source = new BufferReplayNode(input);
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
        // A NaN input stays NaN through any gain, so the sanitizer must replace it with 0.
        const int sampleCount = SampleRate / 4;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);
        input.GetChannelData(0)[0] = float.NaN;
        input.GetChannelData(1)[0] = float.NaN;

        var source = new BufferReplayNode(input);
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
        // Soft knee attenuates before the input crosses the threshold; hard knee doesn't. Feeding a
        // sine right at the threshold, soft-knee output should be lower — confirming the quadratic
        // in-knee region engages.
        const int sampleCount = SampleRate / 2;
        // 0.1 amplitude → exactly -20 dB peak, matching the threshold.
        using var input = CreateSineBuffer(0.1f, 1000f, sampleCount);

        var hardKneeNode = CreateNode(threshold: -20f, ratio: 4f, knee: 0f);
        hardKneeNode.AddInput(new BufferReplayNode(input));
        using var hardOutput = hardKneeNode.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)));

        var softKneeNode = CreateNode(threshold: -20f, ratio: 4f, knee: 12f);
        softKneeNode.AddInput(new BufferReplayNode(input));
        using var softOutput = softKneeNode.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)));

        int steadyStart = sampleCount / 2;
        float hardPeakDb = PeakDb(hardOutput, steadyStart);
        float softPeakDb = PeakDb(softOutput, steadyStart);

        // Soft knee engages before the threshold, so its peak should be measurably lower than hard.
        Assert.That(softPeakDb, Is.LessThan(hardPeakDb - 0.3f),
            $"Soft knee should attenuate near threshold (hard≈{hardPeakDb:F2} dB, soft≈{softPeakDb:F2} dB)");
    }

    [Test]
    public void Process_MonoBuffer_ProducesExpectedGainReduction()
    {
        // A single-channel buffer must work with no off-by-one and reach the stereo compression level.
        const int sampleCount = SampleRate;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount, channels: 1);
        var source = new BufferReplayNode(input);

        var node = CreateNode();
        node.AddInput(source);

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));
        using var output = node.Process(ctx);

        Assert.That(output.ChannelCount, Is.EqualTo(1));
        float steadyPeakDb = PeakDb(output, sampleCount / 2);
        Assert.That(steadyPeakDb, Is.EqualTo(-13.5f).Within(2.5f));
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
        // Animate Attack 1 ms → 200 ms so every sample invalidates the coefficient cache and forces
        // ComputeCoeff to recompute. A late step input lands while the slow attack is in effect, so
        // the unclamped transient peak must exceed the settled tail. A stuck 1 ms attack would clamp
        // instantly and erase that difference.
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
        node.AddInput(new BufferReplayNode(input));

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
        // ProcessAnimated walks the input in fixed-size chunks. A buffer straddling several chunk
        // boundaries must show no discontinuity at boundary indices for steady input — a jump there
        // would mean the envelope was reset at the boundary.
        const int chunkSize = 1024;
        const int sampleCount = chunkSize * 3 + 137; // straddles several boundaries
        using var input = CreateConstantBuffer(0.9f, sampleCount);

        // Trivial threshold animation (constant value) just to force the ProcessAnimated path.
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
        node.AddInput(new BufferReplayNode(input));

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate));
        using var output = node.Process(ctx);

        // At each boundary the adjacent-sample delta must stay bounded by the prior delta — no jump
        // from a boundary state reset.
        var data = output.GetChannelData(0);
        for (int boundary = chunkSize; boundary < sampleCount; boundary += chunkSize)
        {
            float prevDelta = MathF.Abs(data[boundary - 1] - data[boundary - 2]);
            float boundaryDelta = MathF.Abs(data[boundary] - data[boundary - 1]);
            // Tolerance absorbs tiny natural variation but rejects an envelope reset (step ≥ ~0.1).
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
        // A NaN/Infinity keyframe on any animated parameter must not reach the sanitizer (which
        // would mute the whole chunk); each must fall back to its static CurrentValue. Every
        // parameter is tested so a missing SafeParameter call on any one is caught.
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
        // IProperty<float> exposes the Animation setter directly, no concrete cast needed.
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
        node.AddInput(new BufferReplayNode(input));

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25));
        using var output = node.Process(ctx);

        // If NaN reached the gain calc the sanitizer would zero everything (-100 dB peak); a peak
        // well above that proves the fallback engaged.
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
        node.AddInput(new BufferReplayNode(bufA));
        node.AddInput(new BufferReplayNode(bufB));
        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.1));
        Assert.Throws<InvalidOperationException>(() => node.Process(ctx));
    }

    [Test]
    public void Process_ZeroLengthInput_Static_ReturnsEmptyBuffer()
    {
        // A zero-length chunk must not divide by zero or stackalloc[0]. No animated parameters, so
        // the static path is exercised.
        using var input = new AudioBuffer(SampleRate, 2, 0);
        var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));
        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.Zero);

        using var output = node.Process(ctx);

        Assert.That(output.SampleCount, Is.EqualTo(0));
        Assert.That(output.ChannelCount, Is.EqualTo(2));
        Assert.That(output.SampleRate, Is.EqualTo(SampleRate));
    }

    [Test]
    public void Process_ZeroLengthInput_Animated_ReturnsEmptyBuffer()
    {
        // Same as above but animated. The zero-length early-return in Process() fires before
        // ProcessAnimated is entered, so this pins the early-return contract — the animated chunk
        // loop is unreachable at SampleCount == 0.
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
        node.AddInput(new BufferReplayNode(input));

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.Zero);
        using var output = node.Process(ctx);

        Assert.That(output.SampleCount, Is.EqualTo(0));
        Assert.That(output.ChannelCount, Is.EqualTo(2));
    }

    [Test]
    public void Process_AnimatedMakeupGain_AppliesPerSampleGain()
    {
        // Sweep MakeupGain 0 → +12 dB over a steady loud signal; the tail must measure ~12 dB louder
        // than the head, proving the animated per-sample makeup is mixed in with the correct sign.
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
        node.AddInput(new BufferReplayNode(input));

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));
        using var output = node.Process(ctx);

        // Compare an early window (makeup ≈ 0 dB) against a late one (≈ +12 dB), both past the attack
        // ramp so the envelope is steady.
        int probeWidth = SampleRate / 100; // 10 ms
        float earlyPeakDb = PeakDbInWindow(output, SampleRate / 4, probeWidth);
        float latePeakDb = PeakDbInWindow(output, sampleCount - probeWidth, probeWidth);

        float observedRise = latePeakDb - earlyPeakDb;
        // 12 dB nominal; wide tolerance because the 25%→100% window covers only ~9 dB plus ripple.
        Assert.That(observedRise, Is.GreaterThan(6f),
            $"Animated MakeupGain should raise output level (early≈{earlyPeakDb:F2} dB, late≈{latePeakDb:F2} dB, rise≈{observedRise:F2} dB)");
        Assert.That(observedRise, Is.LessThan(15f),
            $"Animated MakeupGain rise is implausibly large (early≈{earlyPeakDb:F2} dB, late≈{latePeakDb:F2} dB)");
    }

    [Test]
    public void Process_AnimatedRatio_BelowOne_ClampsToPassthrough()
    {
        // The animated clamp must mirror the static path: ratio 0.5 gives slope -1 (amplifies above
        // threshold); clamping to MinRatio=1 makes slope 0 → passthrough.
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
        node.AddInput(new BufferReplayNode(input));

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
        // An animated Attack of 1e9 ms would collapse the coefficient to 1.0, freezing the envelope
        // so it never compresses. Clamping to MaxAttackMs keeps coeff < 1.0 so the envelope advances.
        // A deep -50 dB threshold lets the slow envelope reach it within 1 s; the output reduction
        // proves the clamp engaged.
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
        node.AddInput(new BufferReplayNode(input));

        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0));
        using var output = node.Process(ctx);

        // Probe the last 50 ms, by when the clamped attack has had ~2 time constants to cross -50 dB.
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
        // Two animated parameters produce NaN at once. A per-parameter HashSet (not a shared latch)
        // ensures both still substitute their fallbacks. Observable: output is not muted (would be
        // -100 dB if a NaN propagated).
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
        node.AddInput(new BufferReplayNode(input));

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
        // ProcessStatic and ProcessAnimated share the same compute helpers so they cannot drift.
        // With a constant Threshold animation the animated path runs but reads the same values as
        // static, so outputs must match sample-for-sample. Catches a coefficient-cache off-by-one,
        // a boundary coefficient reset, or a slope re-derivation bug that the smoothness-only test
        // would miss.
        const int sampleCount = SampleRate / 2; // straddles many 1024-sample chunks
        var duration = TimeSpan.FromSeconds(sampleCount / (double)SampleRate);
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);

        // Static reference path (knee>0 and makeup!=0 so both branches are exercised in both paths).
        var staticNode = CreateNode(threshold: -20f, ratio: 4f, attack: 5f, release: 50f, knee: 6f, makeup: 3f);
        staticNode.AddInput(new BufferReplayNode(input));
        using var staticOut = staticNode.Process(CreateContext(TimeSpan.Zero, duration));

        // Animated path forced on via a constant Threshold animation; the other parameters stay
        // unanimated, so AnimationSampler fills each with the same CurrentValue static reads.
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
        animatedNode.AddInput(new BufferReplayNode(input));
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
        // Validate the soft-knee quadratic numerically, not just "soft < hard". A DC signal at the
        // threshold drives the envelope to thresholdDb (diff = 0, mid-knee), where the closed form
        // is GR = slope * halfKnee^2 / (2*knee). Hard knee gives 0 dB there, so this point uniquely
        // exercises the quadratic branch of ComputeGainReductionDb.
        const int sampleCount = SampleRate / 2; // long enough for the envelope to settle to inputDb
        const float thresholdDb = -20f;
        const float ratio = 4f;
        const float kneeDb = 12f;
        float amplitude = MathF.Pow(10f, thresholdDb / 20f); // 0.1 → peak dB == threshold
        using var input = CreateConstantBuffer(amplitude, sampleCount);

        var node = CreateNode(threshold: thresholdDb, ratio: ratio, attack: 1f, release: 1f, knee: kneeDb, makeup: 0f);
        node.AddInput(new BufferReplayNode(input));
        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate)));

        // Recover GR from the settled output and compare to the closed form at diff = 0.
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
        // hasAnimation keys solely on Animation != null, so an expression-backed property routes to
        // ProcessStatic and reads CurrentValue without evaluating the expression. Output must equal
        // a fully-static node with the same CurrentValue. If a future change makes HasExpression
        // live (the FIXME's goal), this forces a deliberate update instead of silently changing audio.
        const int sampleCount = SampleRate / 2;
        var duration = TimeSpan.FromSeconds(sampleCount / (double)SampleRate);
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);

        var staticNode = CreateNode(threshold: -20f);
        staticNode.AddInput(new BufferReplayNode(input));
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
        exprNode.AddInput(new BufferReplayNode(input));
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

    [Test]
    public void Process_NonFiniteSampleLatch_SurvivesSeekDiscontinuity()
    {
        // The diagnostic latch must be PRESERVED across a seek discontinuity: a stuttering scrubber
        // produces many seeks, and re-arming on each would re-log a persistent fault every Process().
        // The seek path calls ResetEnvelope() only (not ResetDiagnostics), so a second seeked chunk
        // with the same fault must emit no second warning.
        var node = CreateNode();
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);

        using var first = MakeInfinityHeadBuffer(chunkSamples);
        node.AddInput(new BufferReplayNode(first));
        using var firstOut = node.Process(CreateContext(TimeSpan.Zero, chunkDuration));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(1),
            "The first non-finite sample must emit exactly one warning.");

        node.ClearInputs();
        using var second = MakeInfinityHeadBuffer(chunkSamples);
        node.AddInput(new BufferReplayNode(second));
        // Non-contiguous start time → ResetEnvelope only; diagnostics are deliberately NOT re-armed.
        using var seekedOut = node.Process(CreateContext(TimeSpan.FromSeconds(5.0), chunkDuration));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(1),
            "A seek discontinuity must NOT re-arm the latch, so no second warning should be emitted.");
    }

    [Test]
    public void Process_NonFiniteSampleLatch_ReArmsOnSampleRateChange()
    {
        // A sample-rate change is a real session boundary: Process() calls full Reset(), which
        // re-arms diagnostics, so the same fault at the new rate must warn again.
        var node = CreateNode();
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);

        using var first = MakeInfinityHeadBuffer(chunkSamples);
        node.AddInput(new BufferReplayNode(first));
        using var firstOut = node.Process(CreateContext(TimeSpan.Zero, chunkDuration));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(1));

        node.ClearInputs();
        const int altSampleRate = 44100;
        using var second = MakeInfinityHeadBuffer(altSampleRate / 10, altSampleRate);
        node.AddInput(new BufferReplayNode(second));
        using var secondOut = node.Process(new AudioProcessContext(
            new TimeRange(chunkDuration, TimeSpan.FromSeconds(0.1)),
            altSampleRate, new AnimationSampler(), null));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(2),
            "A sample-rate change re-arms the latch, so the recurring fault must warn a second time.");
    }

    [Test]
    public void Reset_ReArmsNonFiniteSampleLatch()
    {
        // Explicit Reset() re-arms diagnostics too. The second chunk stays time-contiguous so only
        // the Reset() — not a discontinuity — can re-arm the latch.
        var node = CreateNode();
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);

        using var first = MakeInfinityHeadBuffer(chunkSamples);
        node.AddInput(new BufferReplayNode(first));
        using var firstOut = node.Process(CreateContext(TimeSpan.Zero, chunkDuration));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(1));

        node.Reset();
        node.ClearInputs();
        using var second = MakeInfinityHeadBuffer(chunkSamples);
        node.AddInput(new BufferReplayNode(second));
        using var secondOut = node.Process(CreateContext(chunkDuration, chunkDuration));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(2),
            "Explicit Reset() re-arms the latch, so the recurring fault must warn a second time.");
    }

    [Test]
    public void Process_LinkedSurround_AppliesLoudestChannelGainToAllChannels()
    {
        // Peak detection links across ALL channels and MapChannels reallocates caches for >2
        // channels (the channel-major fallback). A 4-channel buffer whose loudest is channel 2
        // (not 0) must attenuate every channel by channel 2's reduction — a loop scanning only
        // channels 0..1, or a channel-bound off-by-one, would fail here.
        const int sampleCount = SampleRate;
        const int channels = 4;
        using var input = new AudioBuffer(SampleRate, channels, sampleCount);
        float[] amps = [0.05f, 0.05f, 0.9f, 0.05f]; // loudest is channel 2
        for (int ch = 0; ch < channels; ch++)
        {
            var data = input.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                data[i] = amps[ch] * MathF.Sin(2f * MathF.PI * 1000f * i / SampleRate);
            }
        }

        var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));
        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0)));

        Assert.That(output.ChannelCount, Is.EqualTo(channels));

        int steadyStart = SampleRate / 2;
        // Channel 2 drives compression to ~-13.5 dB (same ±2.5 band as the stereo case).
        float loudestPeakDb = ChannelPeakDb(output, 2, steadyStart);
        Assert.That(loudestPeakDb, Is.EqualTo(-13.5f).Within(2.5f),
            "Sanity check: the loudest channel (2) must be compressed for the linked-gain expectation to be meaningful.");

        float loudestInputPeakDb = 20f * MathF.Log10(0.9f);
        float gainReductionDb = loudestInputPeakDb - loudestPeakDb;
        float quietInputPeakDb = 20f * MathF.Log10(0.05f);
        float expectedQuietDb = quietInputPeakDb - gainReductionDb;

        // Every quiet channel must receive the SAME gain reduction derived from channel 2.
        foreach (int ch in new[] { 0, 1, 3 })
        {
            float quietPeakDb = ChannelPeakDb(output, ch, steadyStart);
            Assert.That(quietPeakDb, Is.EqualTo(expectedQuietDb).Within(1.5f),
                $"Channel {ch} should be attenuated by channel 2's linked gain reduction.");
        }
    }

    [Test]
    public void Process_AnimatedOutOfRangeParameter_LogsClampWarningOncePerParameter()
    {
        // A finite out-of-[Range] animated value (Attack = 1e9 ms) is clamped and must emit exactly
        // one clamp warning — not zero (hidden misconfiguration), not per-sample (audio-thread spam).
        // The other five parameters stay in range, isolating the count to the offending one.
        const int sampleCount = SampleRate / 4;
        using var input = CreateSineBuffer(0.9f, 1000f, sampleCount);

        var attackAnim = new KeyFrameAnimation<float>();
        attackAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 1e9f, KeyTime = TimeSpan.Zero });
        attackAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 1e9f, KeyTime = TimeSpan.FromSeconds(0.25) });
        var attackProperty = Property.CreateAnimatable(5f);
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
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25)));

        Assert.That(node.ClampWarnings, Is.EqualTo(1),
            "An out-of-range animated Attack must warn exactly once for the whole chunk, not zero and not per-sample.");
    }
}
