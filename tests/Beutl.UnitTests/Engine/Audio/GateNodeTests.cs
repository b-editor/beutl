using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

using static Beutl.UnitTests.Engine.Audio.AudioTestBuffers;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class GateNodeTests
{
    private const int SampleRate = 48000;

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

    private static GateNode CreateNode(
        float threshold = -40f,
        float attack = 1f,
        float hold = 10f,
        float release = 50f,
        float range = -60f)
    {
        return new GateNode
        {
            Threshold = Property.CreateAnimatable(threshold),
            Attack = Property.CreateAnimatable(attack),
            Hold = Property.CreateAnimatable(hold),
            Release = Property.CreateAnimatable(release),
            Range = Property.CreateAnimatable(range)
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

    // Two-phase buffer: a loud constant for the first half then a quiet constant for the second, used
    // to drive the gate open and then observe how it releases against the quiet tail.
    private static AudioBuffer MakeTwoPhaseBuffer(float loud, float quiet, int loudSamples, int totalSamples)
    {
        return CreateBuffer(2, totalSamples, (_, i) => i < loudSamples ? loud : quiet);
    }

    [Test]
    public void Process_SilenceInput_ProducesExactSilenceOutput()
    {
        // A gate multiplies each sample by its gain, so a zero input must yield exact-zero output
        // regardless of the internal gate state.
        const int sampleCount = SampleRate / 4;
        using var input = new AudioBuffer(SampleRate, 2, sampleCount);
        var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25)));

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
    public void Process_AboveThreshold_OpensAndPassesSignalThrough()
    {
        // A steady tone well above the threshold opens the gate; the settled tail should reach unity
        // gain (output peak ≈ input peak).
        const int sampleCount = SampleRate / 2;
        using var input = CreateConstantBuffer(0.9f, sampleCount);
        var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)));

        float inputPeakDb = PeakDb(input, 0);
        float outputPeakDb = PeakDb(output, sampleCount / 2);
        Assert.That(outputPeakDb, Is.EqualTo(inputPeakDb).Within(1f),
            $"Above-threshold signal should pass at unity (input≈{inputPeakDb:F2} dB, output≈{outputPeakDb:F2} dB)");
    }

    [Test]
    public void Process_BelowThreshold_AttenuatesTowardRange()
    {
        // A steady tone below the threshold keeps the gate closed, so the tail is attenuated by close
        // to the Range depth (here ≈60 dB below input).
        const int sampleCount = SampleRate / 2;
        using var input = CreateConstantBuffer(0.003f, sampleCount); // ≈-50 dB, below -40 threshold
        var node = CreateNode(range: -60f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)));

        float inputPeakDb = PeakDb(input, 0);
        float outputPeakDb = PeakDb(output, sampleCount / 2);
        Assert.That(outputPeakDb, Is.LessThan(inputPeakDb - 40f),
            $"Below-threshold signal should be heavily attenuated (input≈{inputPeakDb:F2} dB, output≈{outputPeakDb:F2} dB)");
    }

    [Test]
    public void Process_RangeZero_DisablesGating()
    {
        // Range = 0 means the closed floor equals the open level, so even a below-threshold signal
        // passes through at unity — gating is effectively disabled.
        const int sampleCount = SampleRate / 2;
        using var input = CreateConstantBuffer(0.01f, sampleCount); // below -40 threshold
        var node = CreateNode(range: 0f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)));

        float inputPeakDb = PeakDb(input, 0);
        float outputPeakDb = PeakDb(output, sampleCount / 2);
        Assert.That(outputPeakDb, Is.EqualTo(inputPeakDb).Within(1f),
            $"Range=0 should disable gating (input≈{inputPeakDb:F2} dB, output≈{outputPeakDb:F2} dB)");
    }

    [Test]
    public void Process_AttackTimeConstant_GainReachesAbout63PercentAfterAttackMs()
    {
        // Opening from the fully-closed floor, a one-pole attack should close ~(1 - 1/e) ≈ 63% of the
        // distance to 0 dB after attackMs, leaving the gain ≈-36.8 dB. Range=-100 makes the closed
        // floor equal the -100 dB start so the silent lead-in does not pre-open the gate. Catches a
        // dropped ms→s conversion in ComputeCoeff, which would leave the gate stuck near -100 dB.
        const float attackMs = 50f;
        const int sampleCount = SampleRate;
        const int stepAt = SampleRate / 10; // step to loud at 100 ms
        using var input = CreateBuffer(1, sampleCount, (_, i) => i < stepAt ? 0f : 0.9f);
        var node = CreateNode(threshold: -40f, attack: attackMs, hold: 0f, release: 100f, range: -100f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1.0)));

        int probeIdx = stepAt + (int)(attackMs * 0.001f * SampleRate);
        float gainLinear = MathF.Abs(output.GetChannelData(0)[probeIdx] / 0.9f);
        float gainDb = 20f * MathF.Log10(gainLinear);

        Assert.That(gainDb, Is.EqualTo(-36.8f).Within(8f),
            $"At t = attackMs ({attackMs} ms), gate gain should reach ≈-36.8 dB but got {gainDb:F2} dB. " +
            $"Near -100 dB indicates ComputeCoeff lost its ms→s conversion.");
    }

    [Test]
    public void Process_Hold_KeepsGateOpenAfterSignalDrops()
    {
        // After a loud burst opens the gate, a long Hold latches it open across a following
        // below-threshold tail, so that quiet tail passes far louder than with Hold=0 (which releases
        // immediately).
        const int loudSamples = SampleRate / 10;  // 100 ms loud
        const int quietSamples = SampleRate / 10;  // 100 ms quiet tail
        const int total = loudSamples + quietSamples;
        using var input = MakeTwoPhaseBuffer(0.9f, 0.005f, loudSamples, total); // quiet ≈-46 dB < -40

        var holdNode = CreateNode(hold: 500f, release: 50f);
        holdNode.AddInput(new BufferReplayNode(input));
        using var holdOut = holdNode.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(total / (double)SampleRate)));

        var noHoldNode = CreateNode(hold: 0f, release: 50f);
        noHoldNode.AddInput(new BufferReplayNode(input));
        using var noHoldOut = noHoldNode.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(total / (double)SampleRate)));

        // Probe the last 40 ms of the quiet tail.
        int probeWidth = SampleRate / 25;
        float holdTailDb = PeakDbInWindow(holdOut, total - probeWidth, probeWidth);
        float noHoldTailDb = PeakDbInWindow(noHoldOut, total - probeWidth, probeWidth);

        Assert.That(holdTailDb, Is.GreaterThan(noHoldTailDb + 20f),
            $"Hold should keep the gate open over the quiet tail (hold≈{holdTailDb:F2} dB, no-hold≈{noHoldTailDb:F2} dB)");
    }

    [Test]
    public void Process_GateStateContinuesAcrossChunks()
    {
        // A gate opened by a previous loud chunk must NOT re-close on a time-contiguous next chunk. The
        // warmed-open gate's first sample is louder than a fresh gate still ramping open from closed.
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);
        var ctx1 = CreateContext(TimeSpan.Zero, chunkDuration);
        var ctx2 = CreateContext(chunkDuration, chunkDuration);

        var nodeContinuing = CreateNode();
        using var warmupInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeContinuing.AddInput(new BufferReplayNode(warmupInput));
        using var warmup = nodeContinuing.Process(ctx1);
        nodeContinuing.ClearInputs();
        using var followInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeContinuing.AddInput(new BufferReplayNode(followInput));
        using var followOutput = nodeContinuing.Process(ctx2);

        var nodeFresh = CreateNode();
        using var freshInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new BufferReplayNode(freshInput));
        using var freshOutput = nodeFresh.Process(ctx1);

        float continuingFirst = MathF.Abs(followOutput.GetChannelData(0)[0]);
        float freshFirst = MathF.Abs(freshOutput.GetChannelData(0)[0]);
        Assert.That(continuingFirst, Is.GreaterThan(freshFirst));
    }

    [Test]
    public void Process_NonContiguousTimeRange_ResetsGate()
    {
        // A non-contiguous start time (a seek) must reset the gate to closed, so the first sample
        // matches a fresh gate's first sample.
        const int chunkSamples = SampleRate / 10;
        using var loud = CreateConstantBuffer(0.9f, chunkSamples);

        var node = CreateNode();
        node.AddInput(new BufferReplayNode(loud));
        var ctx1 = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(chunkSamples / (double)SampleRate));
        using var firstOutput = node.Process(ctx1);

        node.ClearInputs();
        using var loud2 = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(loud2));
        var ctxSeek = CreateContext(TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(chunkSamples / (double)SampleRate));
        using var seekedOutput = node.Process(ctxSeek);

        var nodeFresh = CreateNode();
        using var loud3 = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new BufferReplayNode(loud3));
        using var freshOutput = nodeFresh.Process(
            CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(chunkSamples / (double)SampleRate)));

        float seekedFirst = MathF.Abs(seekedOutput.GetChannelData(0)[0]);
        float freshFirst = MathF.Abs(freshOutput.GetChannelData(0)[0]);
        Assert.That(seekedFirst, Is.EqualTo(freshFirst).Within(1e-4f));
    }

    [Test]
    public void Process_SampleRateChange_ResetsGate()
    {
        const int chunkSamples = SampleRate / 10;
        using var loud = CreateConstantBuffer(0.9f, chunkSamples);

        var node = CreateNode();
        node.AddInput(new BufferReplayNode(loud));
        var ctx48 = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(chunkSamples / (double)SampleRate));
        using var firstOutput = node.Process(ctx48);

        node.ClearInputs();
        const int altSampleRate = 44100;
        using var loud44 = CreateConstantBuffer(0.9f, altSampleRate / 10, 2, altSampleRate);
        node.AddInput(new BufferReplayNode(loud44));
        var ctx44 = new AudioProcessContext(
            new TimeRange(TimeSpan.FromSeconds(chunkSamples / (double)SampleRate), TimeSpan.FromSeconds(0.1)),
            altSampleRate,
            new AnimationSampler(),
            null);
        using var secondOutput = node.Process(ctx44);

        var nodeFresh = CreateNode();
        using var freshInput = CreateConstantBuffer(0.9f, altSampleRate / 10, 2, altSampleRate);
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
    public void Reset_ClearsGateState()
    {
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);
        var ctx1 = CreateContext(TimeSpan.Zero, chunkDuration);
        var ctx2 = CreateContext(chunkDuration, chunkDuration);

        var node = CreateNode();
        using var warmupInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(warmupInput));
        using var firstOutput = node.Process(ctx1);

        node.Reset();
        node.ClearInputs();
        using var followInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(followInput));
        using var afterResetOutput = node.Process(ctx2);

        var nodeFresh = CreateNode();
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
        // Threshold animates from -60 dB (below the -40 dB input → gate open) to -20 dB (above it →
        // gate closed), so a steady quiet tone goes from passing to attenuated: the late portion is
        // quieter, proving the animated path runs.
        const int sampleCount = SampleRate / 2;
        using var input = CreateConstantBuffer(0.01f, sampleCount); // ≈-40 dB

        var thresholdAnim = new KeyFrameAnimation<float>();
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -60f, KeyTime = TimeSpan.Zero });
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -20f, KeyTime = TimeSpan.FromSeconds(0.5) });
        var thresholdProperty = Property.CreateAnimatable(-60f);
        thresholdProperty.Animation = thresholdAnim;

        var node = new GateNode
        {
            Threshold = thresholdProperty,
            Attack = Property.CreateAnimatable(1f),
            Hold = Property.CreateAnimatable(0f),
            Release = Property.CreateAnimatable(20f),
            Range = Property.CreateAnimatable(-60f)
        };
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)));

        int lastQuarterStart = sampleCount * 3 / 4;
        float earlyPeakDb = PeakDbInWindow(output, sampleCount / 8, sampleCount / 8);
        float latePeakDb = PeakDb(output, lastQuarterStart);
        Assert.That(latePeakDb, Is.LessThan(earlyPeakDb - 2f),
            $"Animated threshold should attenuate the late portion (early≈{earlyPeakDb:F2} dB, late≈{latePeakDb:F2} dB)");
    }

    [Test]
    public void Process_StaticAndAnimatedPaths_ProduceIdenticalOutputForConstantParameters()
    {
        // ProcessStatic and ProcessAnimated share the same gate helpers, so with a constant Threshold
        // animation the animated path must match the static path sample-for-sample. A two-phase buffer
        // exercises both an opening and a releasing transition.
        const int loudSamples = SampleRate / 4;
        const int sampleCount = SampleRate / 2;
        var duration = TimeSpan.FromSeconds(sampleCount / (double)SampleRate);
        using var input = MakeTwoPhaseBuffer(0.9f, 0.003f, loudSamples, sampleCount);

        var staticNode = CreateNode(threshold: -40f, attack: 2f, hold: 5f, release: 30f, range: -50f);
        staticNode.AddInput(new BufferReplayNode(input));
        using var staticOut = staticNode.Process(CreateContext(TimeSpan.Zero, duration));

        var thresholdAnim = new KeyFrameAnimation<float>();
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -40f, KeyTime = TimeSpan.Zero });
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -40f, KeyTime = duration });
        var thresholdProperty = Property.CreateAnimatable(-40f);
        thresholdProperty.Animation = thresholdAnim;

        var animatedNode = new GateNode
        {
            Threshold = thresholdProperty,
            Attack = Property.CreateAnimatable(2f),
            Hold = Property.CreateAnimatable(5f),
            Release = Property.CreateAnimatable(30f),
            Range = Property.CreateAnimatable(-50f)
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
    public void Process_InfinityInputSamples_RecoversAndDoesNotLeakNonFiniteOutput()
    {
        // A +Infinity head sample pollutes the gain follower; the self-recovery clamp and the output
        // sanitizer must keep any NaN/Infinity from escaping downstream.
        const int sampleCount = SampleRate / 4;
        using var input = CreateConstantBuffer(0.9f, sampleCount);
        for (int ch = 0; ch < input.ChannelCount; ch++)
        {
            input.GetChannelData(ch)[0] = float.PositiveInfinity;
            input.GetChannelData(ch)[1] = float.PositiveInfinity;
        }

        var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));
        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25)));

        for (int ch = 0; ch < output.ChannelCount; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < output.SampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"Output sample [{ch}][{i}] = {data[i]} is not finite");
            }
        }
    }

    [Test]
    public void Process_NaNInputSamples_ProducesFiniteOutput()
    {
        const int sampleCount = SampleRate / 4;
        using var input = CreateConstantBuffer(0.9f, sampleCount);
        input.GetChannelData(0)[0] = float.NaN;
        input.GetChannelData(1)[0] = float.NaN;

        var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));
        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25)));

        Assert.That(output.GetChannelData(0)[0], Is.EqualTo(0f));
        Assert.That(output.GetChannelData(1)[0], Is.EqualTo(0f));
        for (int ch = 0; ch < output.ChannelCount; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 1; i < output.SampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True);
            }
        }
    }

    public enum AnimatedParam { Threshold, Attack, Hold, Release, Range }

    [TestCase(AnimatedParam.Threshold)]
    [TestCase(AnimatedParam.Attack)]
    [TestCase(AnimatedParam.Hold)]
    [TestCase(AnimatedParam.Release)]
    [TestCase(AnimatedParam.Range)]
    public void Process_AnimatedNonFiniteValue_FallsBackWithoutMuting(AnimatedParam param)
    {
        // A NaN keyframe on any animated parameter must fall back to its static CurrentValue rather
        // than reach the gate math and mute the output. A loud above-threshold signal should keep
        // passing.
        const int sampleCount = SampleRate / 4;
        using var input = CreateConstantBuffer(0.9f, sampleCount);

        var threshold = Property.CreateAnimatable(-40f);
        var attack = Property.CreateAnimatable(1f);
        var hold = Property.CreateAnimatable(10f);
        var release = Property.CreateAnimatable(50f);
        var range = Property.CreateAnimatable(-60f);

        IProperty<float> target = param switch
        {
            AnimatedParam.Threshold => threshold,
            AnimatedParam.Attack => attack,
            AnimatedParam.Hold => hold,
            AnimatedParam.Release => release,
            AnimatedParam.Range => range,
            _ => throw new ArgumentOutOfRangeException(nameof(param))
        };
        var anim = new KeyFrameAnimation<float>();
        anim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = float.NaN, KeyTime = TimeSpan.Zero });
        anim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = float.NaN, KeyTime = TimeSpan.FromSeconds(0.25) });
        target.Animation = anim;

        var node = new GateNode
        {
            Threshold = threshold,
            Attack = attack,
            Hold = hold,
            Release = release,
            Range = range
        };
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25)));

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
        Assert.That(steadyPeakDb, Is.GreaterThan(-6f),
            $"Fallback failed for {param}: above-threshold output appears to have been muted");
    }

    [Test]
    public void Process_MonoBuffer_GatesCorrectly()
    {
        // A single-channel buffer must gate with no off-by-one: above-threshold passes, below-threshold
        // is attenuated.
        const int sampleCount = SampleRate / 2;
        using var loud = CreateConstantBuffer(0.9f, sampleCount, channels: 1);
        var loudNode = CreateNode();
        loudNode.AddInput(new BufferReplayNode(loud));
        using var loudOut = loudNode.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)));

        using var quiet = CreateConstantBuffer(0.003f, sampleCount, channels: 1);
        var quietNode = CreateNode();
        quietNode.AddInput(new BufferReplayNode(quiet));
        using var quietOut = quietNode.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)));

        Assert.That(loudOut.ChannelCount, Is.EqualTo(1));
        Assert.That(PeakDb(loudOut, sampleCount / 2), Is.EqualTo(PeakDb(loud, 0)).Within(1f));
        Assert.That(PeakDb(quietOut, sampleCount / 2), Is.LessThan(PeakDb(quiet, 0) - 40f));
    }

    [Test]
    public void Process_NoInputs_Throws()
    {
        var node = CreateNode();
        var ctx = CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.1));
        Assert.Throws<InvalidOperationException>(() => node.Process(ctx));
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
        using var input = new AudioBuffer(SampleRate, 2, 0);
        var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));
        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.Zero));

        Assert.That(output.SampleCount, Is.EqualTo(0));
        Assert.That(output.ChannelCount, Is.EqualTo(2));
        Assert.That(output.SampleRate, Is.EqualTo(SampleRate));
    }

    [Test]
    public void Process_ZeroLengthInput_Animated_ReturnsEmptyBuffer()
    {
        using var input = new AudioBuffer(SampleRate, 2, 0);

        var thresholdAnim = new KeyFrameAnimation<float>();
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -40f, KeyTime = TimeSpan.Zero });
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -20f, KeyTime = TimeSpan.FromSeconds(1.0) });
        var thresholdProperty = Property.CreateAnimatable(-40f);
        thresholdProperty.Animation = thresholdAnim;

        var node = new GateNode
        {
            Threshold = thresholdProperty,
            Attack = Property.CreateAnimatable(1f),
            Hold = Property.CreateAnimatable(10f),
            Release = Property.CreateAnimatable(50f),
            Range = Property.CreateAnimatable(-60f)
        };
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.Zero));

        Assert.That(output.SampleCount, Is.EqualTo(0));
        Assert.That(output.ChannelCount, Is.EqualTo(2));
    }

    [Test]
    public void Process_AnimatedPath_SmoothAcrossChunkBoundary()
    {
        // ProcessAnimated walks the input in fixed-size chunks. A steady loud input must show no
        // discontinuity at chunk boundaries — a jump there would mean the gate was reset at a boundary.
        const int chunkSize = 1024;
        const int sampleCount = chunkSize * 3 + 137;
        using var input = CreateConstantBuffer(0.9f, sampleCount);

        var thresholdAnim = new KeyFrameAnimation<float>();
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -40f, KeyTime = TimeSpan.Zero });
        thresholdAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = -40f, KeyTime = TimeSpan.FromSeconds(sampleCount / (double)SampleRate) });
        var thresholdProperty = Property.CreateAnimatable(-40f);
        thresholdProperty.Animation = thresholdAnim;

        var node = new GateNode
        {
            Threshold = thresholdProperty,
            Attack = Property.CreateAnimatable(5f),
            Hold = Property.CreateAnimatable(10f),
            Release = Property.CreateAnimatable(50f),
            Range = Property.CreateAnimatable(-60f)
        };
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate)));

        var data = output.GetChannelData(0);
        for (int boundary = chunkSize; boundary < sampleCount; boundary += chunkSize)
        {
            float prevDelta = MathF.Abs(data[boundary - 1] - data[boundary - 2]);
            float boundaryDelta = MathF.Abs(data[boundary] - data[boundary - 1]);
            Assert.That(boundaryDelta, Is.LessThanOrEqualTo(prevDelta + 0.01f),
                $"Discontinuity at chunk boundary {boundary}: prevDelta={prevDelta:F6}, boundaryDelta={boundaryDelta:F6}");
        }
    }

    [Test]
    public void Process_RangeZero_IsExactIdentity()
    {
        // Range=0 disables gating, so output must equal input sample-for-sample — no startup attack
        // ramp and no asymptotic sub-unity gain from the envelope follower. The buffer includes the
        // initial samples (which would otherwise ramp up from the closed floor) and a below-threshold
        // region (which a real gate would attenuate), so any residual smoothing would show up.
        const int loudSamples = SampleRate / 4;
        const int sampleCount = SampleRate / 2;
        using var input = MakeTwoPhaseBuffer(0.9f, 0.003f, loudSamples, sampleCount);
        var node = CreateNode(range: 0f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate)));

        for (int ch = 0; ch < input.ChannelCount; ch++)
        {
            var inData = input.GetChannelData(ch);
            var outData = output.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(outData[i], Is.EqualTo(inData[i]),
                    $"Range=0 must be exact identity, but [{ch}][{i}] output={outData[i]} != input={inData[i]}");
            }
        }
    }

    [Test]
    public void Process_OneChannelNaN_GatesFromValidChannel()
    {
        // A NaN in one channel must not poison linked-stereo peak detection: a loud, above-threshold
        // valid channel must still open the gate and pass through, rather than the gate closing
        // because Abs(NaN) contaminated the peak. All output stays finite (the NaN channel is zeroed).
        const int sampleCount = SampleRate / 2;
        using var input = CreateBuffer(2, sampleCount, (ch, _) => ch == 0 ? float.NaN : 0.9f);
        var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate)));

        for (int ch = 0; ch < output.ChannelCount; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < output.SampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"Output sample [{ch}][{i}] = {data[i]} is not finite");
            }
        }

        float validPeak = 0f;
        var validData = output.GetChannelData(1);
        for (int i = sampleCount / 2; i < sampleCount; i++)
        {
            float a = MathF.Abs(validData[i]);
            if (a > validPeak) validPeak = a;
        }
        float validPeakDb = validPeak > 0f ? 20f * MathF.Log10(validPeak) : -100f;
        Assert.That(validPeakDb, Is.EqualTo(20f * MathF.Log10(0.9f)).Within(1f),
            $"Valid channel should open the gate and pass at unity despite NaN in the other channel; got {validPeakDb:F2} dB");
    }

    [Test]
    public void Process_MinThreshold_DigitalSilenceDoesNotOpenGate()
    {
        // At the -100 dB minimum Threshold, digital silence (sample 0 = -inf dB) must still leave the
        // gate closed. If silence wrongly opened it, the following loud region would already be open
        // and pass at unity immediately; with the gate correctly closed it ramps open (attack), so the
        // loud onset starts heavily attenuated.
        const int silenceSamples = SampleRate / 5; // 200 ms
        const int sampleCount = SampleRate / 2;
        using var input = CreateBuffer(1, sampleCount, (_, i) => i < silenceSamples ? 0f : 0.9f);
        var node = CreateNode(threshold: -100f, attack: 50f, hold: 0f, release: 100f, range: -60f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate)));

        // First 1 ms of the loud region: a correctly-closed gate is only starting to open, so the peak
        // there is far below unity. A gate wrongly opened during silence would already sit near 0.9.
        int probeWidth = SampleRate / 1000;
        float onsetPeakDb = PeakDbInWindow(output, silenceSamples, probeWidth);
        Assert.That(onsetPeakDb, Is.LessThan(-20f),
            $"Digital silence at min threshold must keep the gate closed; loud onset should ramp from closed but measured {onsetPeakDb:F2} dB");
    }

    [Test]
    public void Process_BelowThresholdLeadIn_StartsAtRangeFloorNotFullMute()
    {
        // A below-threshold lead-in must rest at the user's Range floor from the very first sample, not
        // fade in from the -100 dB reset sentinel. With a shallow Range (-20 dB) and a slow attack the
        // buggy code seeded the follower near -100 dB and only ramped up toward -20, over-attenuating
        // the opening; the first sample's gain should already sit at the -20 dB floor.
        const int sampleCount = SampleRate / 10;
        const float rangeDb = -20f;
        const float amplitude = 0.01f; // ≈-40 dB, below the -30 dB threshold
        using var input = CreateConstantBuffer(amplitude, sampleCount);
        var node = CreateNode(threshold: -30f, attack: 100f, hold: 0f, release: 100f, range: rangeDb);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate)));

        float firstGainDb = 20f * MathF.Log10(MathF.Abs(output.GetChannelData(0)[0]) / amplitude);
        Assert.That(firstGainDb, Is.EqualTo(rangeDb).Within(2f),
            $"Below-threshold lead-in should start at the Range floor ({rangeDb} dB), but first-sample gain was " +
            $"{firstGainDb:F2} dB (a value near -100 dB indicates a fade-in from the reset sentinel).");
    }

    [Test]
    public void Process_OneTickBoundaryRounding_DoesNotResetGate()
    {
        // Adjacent sample-boundary chunks can differ by one tick from independent TimeSpan rounding. A
        // one-tick gap must NOT be treated as a seek: the warmed-open gate must continue (its first
        // sample stays loud) rather than reset to closed like a fresh gate. Buggy exact-equality would
        // reset here, dropping the continuing gate's first sample to the fresh gate's value.
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);
        var ctx1 = CreateContext(TimeSpan.Zero, chunkDuration);
        // Second chunk starts one tick before the exact previous end — within the rounding tolerance.
        var ctx2 = CreateContext(chunkDuration - TimeSpan.FromTicks(1), chunkDuration);

        var node = CreateNode();
        using var warmupInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(warmupInput));
        using var warmup = node.Process(ctx1);
        node.ClearInputs();
        using var followInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(followInput));
        using var followOutput = node.Process(ctx2);

        var nodeFresh = CreateNode();
        using var freshInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new BufferReplayNode(freshInput));
        using var freshOutput = nodeFresh.Process(ctx1);

        float continuingFirst = MathF.Abs(followOutput.GetChannelData(0)[0]);
        float freshFirst = MathF.Abs(freshOutput.GetChannelData(0)[0]);
        Assert.That(continuingFirst, Is.GreaterThan(freshFirst),
            $"A one-tick boundary rounding must not reset the gate (continuing≈{continuingFirst:F4}, fresh≈{freshFirst:F4}).");
    }
}
