using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

using static Beutl.Audio.Effects.GateParameters;
using static Beutl.UnitTests.Engine.Audio.AudioTestAnimations;
using static Beutl.UnitTests.Engine.Audio.AudioTestBuffers;
using static Beutl.UnitTests.Engine.Audio.AudioTestContexts;
using static Beutl.UnitTests.Engine.Audio.AudioTestLevels;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class GateNodeTests
{
    private const int SampleRate = 48000;

    private static float MinGainWhereInputIsAudible(
        AudioBuffer input, AudioBuffer output, int startSample, float minInputAbs)
    {
        var inData = input.GetChannelData(0);
        var outData = output.GetChannelData(0);
        float min = float.MaxValue;
        for (int i = startSample; i < input.SampleCount; i++)
        {
            float a = MathF.Abs(inData[i]);
            if (a < minInputAbs) continue;
            float gain = MathF.Abs(outData[i]) / a;
            if (gain < min) min = gain;
        }
        return min;
    }

    internal static GateNode CreateNode(
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

    private static AudioBuffer MakeTwoPhaseBuffer(float loud, float quiet, int loudSamples, int totalSamples)
    {
        return CreateBuffer(2, totalSamples, (_, i) => i < loudSamples ? loud : quiet);
    }

    [Test]
    public void Process_SilenceInput_ProducesExactSilenceOutput()
    {
        const int sampleCount = SampleRate / 4;
        using var input = new AudioBuffer(SampleRate, 2, sampleCount);
        using var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.25)));

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
        const int sampleCount = SampleRate / 2;
        using var input = CreateConstantBuffer(0.9f, sampleCount);
        using var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.5)));

        float inputPeakDb = PeakDb(input, 0);
        float outputPeakDb = PeakDb(output, sampleCount / 2);
        Assert.That(outputPeakDb, Is.EqualTo(inputPeakDb).Within(1f),
            $"Above-threshold signal should pass at unity (input≈{inputPeakDb:F2} dB, output≈{outputPeakDb:F2} dB)");
    }

    [Test]
    public void Process_BelowThreshold_AttenuatesTowardRange()
    {
        const int sampleCount = SampleRate / 2;
        using var input = CreateConstantBuffer(0.003f, sampleCount); // ≈-50 dB, below -40 threshold
        using var node = CreateNode(range: -60f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.5)));

        float inputPeakDb = PeakDb(input, 0);
        float outputPeakDb = PeakDb(output, sampleCount / 2);
        Assert.That(outputPeakDb, Is.LessThan(inputPeakDb - 40f),
            $"Below-threshold signal should be heavily attenuated (input≈{inputPeakDb:F2} dB, output≈{outputPeakDb:F2} dB)");
    }

    [Test]
    public void Process_AttackTimeConstant_GainReachesAbout63PercentAfterAttackMs()
    {
        // After one attack time constant, gain covers 1 - 1/e of the distance to 0 dB.
        const float attackMs = 50f;
        const int sampleCount = SampleRate;
        const int stepAt = SampleRate / 10;
        using var input = CreateBuffer(1, sampleCount, (_, i) => i < stepAt ? 0f : 0.9f);
        using var node = CreateNode(threshold: -40f, attack: attackMs, hold: 0f, release: 100f, range: -100f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(1.0)));

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
        const int loudSamples = SampleRate / 10;
        const int quietSamples = SampleRate / 10;
        const int total = loudSamples + quietSamples;
        using var input = MakeTwoPhaseBuffer(0.9f, 0.005f, loudSamples, total); // quiet ≈-46 dB < -40

        using var holdNode = CreateNode(hold: 500f, release: 50f);
        holdNode.AddInput(new BufferReplayNode(input));
        using var holdOut = holdNode.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(total / (double)SampleRate)));

        using var noHoldNode = CreateNode(hold: 0f, release: 50f);
        noHoldNode.AddInput(new BufferReplayNode(input));
        using var noHoldOut = noHoldNode.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(total / (double)SampleRate)));

        int probeWidth = SampleRate / 25;
        float holdTailDb = PeakDb(holdOut, total - probeWidth, probeWidth);
        float noHoldTailDb = PeakDb(noHoldOut, total - probeWidth, probeWidth);

        Assert.That(holdTailDb, Is.GreaterThan(noHoldTailDb + 20f),
            $"Hold should keep the gate open over the quiet tail (hold≈{holdTailDb:F2} dB, no-hold≈{noHoldTailDb:F2} dB)");
    }

    [Test]
    public void Process_NonContiguousTimeRange_ResetsGate()
    {
        const int chunkSamples = SampleRate / 10;
        using var loud = CreateConstantBuffer(0.9f, chunkSamples);

        using var node = CreateNode();
        node.AddInput(new BufferReplayNode(loud));
        var ctx1 = CreateContext(MediaTime.Zero, MediaTime.FromSeconds(chunkSamples / (double)SampleRate));
        using var firstOutput = node.Process(ctx1);

        node.ClearInputs();
        using var loud2 = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(loud2));
        var ctxSeek = CreateContext(MediaTime.FromSeconds(5.0), MediaTime.FromSeconds(chunkSamples / (double)SampleRate));
        using var seekedOutput = node.Process(ctxSeek);

        using var nodeFresh = CreateNode();
        using var loud3 = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new BufferReplayNode(loud3));
        using var freshOutput = nodeFresh.Process(
            CreateContext(MediaTime.Zero, MediaTime.FromSeconds(chunkSamples / (double)SampleRate)));

        float seekedFirst = MathF.Abs(seekedOutput.GetChannelData(0)[0]);
        float freshFirst = MathF.Abs(freshOutput.GetChannelData(0)[0]);
        Assert.That(seekedFirst, Is.EqualTo(freshFirst).Within(1e-4f));
    }

    [Test]
    public void Process_SampleRateChange_ResetsGate()
    {
        const int chunkSamples = SampleRate / 10;
        using var loud = CreateConstantBuffer(0.9f, chunkSamples);

        using var node = CreateNode();
        node.AddInput(new BufferReplayNode(loud));
        var ctx48 = CreateContext(MediaTime.Zero, MediaTime.FromSeconds(chunkSamples / (double)SampleRate));
        using var firstOutput = node.Process(ctx48);

        node.ClearInputs();
        const int altSampleRate = 44100;
        using var loud44 = CreateConstantBuffer(0.9f, altSampleRate / 10, 2, altSampleRate);
        node.AddInput(new BufferReplayNode(loud44));
        var ctx44 = new AudioProcessContext(
            new TimeRange(MediaTime.FromSeconds(chunkSamples / (double)SampleRate), MediaTime.FromSeconds(0.1)),
            altSampleRate,
            new AnimationSampler(),
            null);
        using var secondOutput = node.Process(ctx44);

        using var nodeFresh = CreateNode();
        using var freshInput = CreateConstantBuffer(0.9f, altSampleRate / 10, 2, altSampleRate);
        nodeFresh.AddInput(new BufferReplayNode(freshInput));
        var ctxFresh = new AudioProcessContext(
            new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(0.1)),
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
        var chunkDuration = MediaTime.FromSeconds(chunkSamples / (double)SampleRate);
        var ctx1 = CreateContext(MediaTime.Zero, chunkDuration);
        var ctx2 = CreateContext(chunkDuration, chunkDuration);

        using var node = CreateNode();
        using var warmupInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(warmupInput));
        using var firstOutput = node.Process(ctx1);

        node.Reset();
        node.ClearInputs();
        using var followInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(followInput));
        using var afterResetOutput = node.Process(ctx2);

        using var nodeFresh = CreateNode();
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
        const int sampleCount = SampleRate / 2;
        using var input = CreateConstantBuffer(0.01f, sampleCount); // ≈-40 dB

        using var node = CreateNode(threshold: -60f, hold: 0f, release: 20f);
        node.Threshold.SetKeyFrameAnimation(Ramp(-60f, -20f, MediaTime.FromSeconds(0.5)));
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.5)));

        int lastQuarterStart = sampleCount * 3 / 4;
        float earlyPeakDb = PeakDb(output, sampleCount / 8, sampleCount / 8);
        float latePeakDb = PeakDb(output, lastQuarterStart);
        Assert.That(latePeakDb, Is.LessThan(earlyPeakDb - 2f),
            $"Animated threshold should attenuate the late portion (early≈{earlyPeakDb:F2} dB, late≈{latePeakDb:F2} dB)");
    }

    [Test]
    public void Process_StaticAndAnimatedPaths_ProduceIdenticalOutputForConstantParameters()
    {
        const int loudSamples = SampleRate / 4;
        const int sampleCount = SampleRate / 2;
        var duration = MediaTime.FromSeconds(sampleCount / (double)SampleRate);
        using var input = MakeTwoPhaseBuffer(0.9f, 0.003f, loudSamples, sampleCount);

        using var staticNode = CreateNode(threshold: -40f, attack: 2f, hold: 5f, release: 30f, range: -50f);
        staticNode.AddInput(new BufferReplayNode(input));
        using var staticOut = staticNode.Process(CreateContext(MediaTime.Zero, duration));

        using var animatedNode = CreateNode(attack: 2f, hold: 5f, release: 30f, range: -50f);
        animatedNode.Threshold.SetKeyFrameAnimation(Ramp(-40f, -40f, duration));
        animatedNode.AddInput(new BufferReplayNode(input));
        using var animatedOut = animatedNode.Process(CreateContext(MediaTime.Zero, duration));

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
        const int sampleCount = SampleRate / 4;
        using var input = CreateConstantBuffer(0.9f, sampleCount);
        for (int ch = 0; ch < input.ChannelCount; ch++)
        {
            input.GetChannelData(ch)[0] = float.PositiveInfinity;
            input.GetChannelData(ch)[1] = float.PositiveInfinity;
        }

        using var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));
        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.25)));

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

        using var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));
        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.25)));

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
        const int sampleCount = SampleRate / 4;
        using var input = CreateConstantBuffer(0.9f, sampleCount);

        using var node = CreateNode();
        IProperty<float> target = param switch
        {
            AnimatedParam.Threshold => node.Threshold,
            AnimatedParam.Attack => node.Attack,
            AnimatedParam.Hold => node.Hold,
            AnimatedParam.Release => node.Release,
            AnimatedParam.Range => node.Range,
            _ => throw new ArgumentOutOfRangeException(nameof(param))
        };
        target.SetKeyFrameAnimation(Ramp(float.NaN, float.NaN, MediaTime.FromSeconds(0.25)));
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.25)));

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
        const int sampleCount = SampleRate / 2;
        using var loud = CreateConstantBuffer(0.9f, sampleCount, channels: 1);
        using var loudNode = CreateNode();
        loudNode.AddInput(new BufferReplayNode(loud));
        using var loudOut = loudNode.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.5)));

        using var quiet = CreateConstantBuffer(0.003f, sampleCount, channels: 1);
        using var quietNode = CreateNode();
        quietNode.AddInput(new BufferReplayNode(quiet));
        using var quietOut = quietNode.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.5)));

        Assert.That(loudOut.ChannelCount, Is.EqualTo(1));
        Assert.That(PeakDb(loudOut, sampleCount / 2), Is.EqualTo(PeakDb(loud, 0)).Within(1f));
        Assert.That(PeakDb(quietOut, sampleCount / 2), Is.LessThan(PeakDb(quiet, 0) - 40f));
    }

    [Test]
    public void Process_AnimatedPath_SmoothAcrossChunkBoundary()
    {
        const int chunkSize = 1024;
        const int sampleCount = chunkSize * 3 + 137;
        using var input = CreateConstantBuffer(0.9f, sampleCount);

        using var node = CreateNode(attack: 5f);
        node.Threshold.SetKeyFrameAnimation(Ramp(-40f, -40f, MediaTime.FromSeconds(sampleCount / (double)SampleRate)));
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(sampleCount / (double)SampleRate)));

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
        const int loudSamples = SampleRate / 4;
        const int sampleCount = SampleRate / 2;
        using var input = MakeTwoPhaseBuffer(0.9f, 0.003f, loudSamples, sampleCount);
        using var node = CreateNode(range: 0f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(sampleCount / (double)SampleRate)));

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
        const int sampleCount = SampleRate / 2;
        using var input = CreateBuffer(2, sampleCount, (ch, _) => ch == 0 ? float.NaN : 0.9f);
        using var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(sampleCount / (double)SampleRate)));

        for (int ch = 0; ch < output.ChannelCount; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < output.SampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"Output sample [{ch}][{i}] = {data[i]} is not finite");
            }
        }

        float validPeakDb = PeakDb(output, sampleCount / 2, channel: 1);
        Assert.That(validPeakDb, Is.EqualTo(20f * MathF.Log10(0.9f)).Within(1f),
            $"Valid channel should open the gate and pass at unity despite NaN in the other channel; got {validPeakDb:F2} dB");
    }

    // Covers linked-peak guards in the stereo/surround and static/animated paths.
    [TestCase(2, 1, false)]
    [TestCase(2, 4, false)]
    [TestCase(2, 4, true)]
    [TestCase(4, 4, false)]
    [TestCase(4, 4, true)]
    public void Process_OneChannelInfinity_DoesNotOpenGateForValidChannels(int channels, int corruptSamples, bool animated)
    {
        // Without the guard, Infinity opens the linked gate for the hold and release window.
        const int sampleCount = SampleRate / 2;
        const float quiet = 0.003f; // ≈-50 dB, below the -40 dB threshold
        const int holdSamples = (int)(10f * 0.001f * SampleRate);
        var duration = MediaTime.FromSeconds(sampleCount / (double)SampleRate);
        using var input = CreateBuffer(channels, sampleCount, (_, _) => quiet);
        for (int i = 0; i < corruptSamples; i++)
        {
            input.GetChannelData(0)[i] = float.PositiveInfinity;
        }

        using var node = CreateNode();
        if (animated)
        {
            node.Threshold.SetKeyFrameAnimation(Ramp(-40f, -40f, duration));
        }
        node.AddInput(new BufferReplayNode(input));
        using var output = node.Process(CreateContext(MediaTime.Zero, duration));

        for (int ch = 0; ch < output.ChannelCount; ch++)
        {
            var data = output.GetChannelData(ch);
            for (int i = 0; i < output.SampleCount; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"Output sample [{ch}][{i}] = {data[i]} is not finite");
            }
        }

        float quietDb = 20f * MathF.Log10(quiet);
        float peakDb = PeakDb(output, corruptSamples, holdSamples);
        Assert.That(peakDb, Is.LessThan(quietDb - 40f),
            $"Every finite channel is below the threshold, so the gate must stay shut after the corrupt " +
            $"sample; the hold window peaked at {peakDb:F2} dB against a {quietDb:F2} dB input.");
    }

    [Test]
    public void Process_MinThreshold_DigitalSilenceDoesNotOpenGate()
    {
        const int silenceSamples = SampleRate / 5;
        const int sampleCount = SampleRate / 2;
        using var input = CreateBuffer(1, sampleCount, (_, i) => i < silenceSamples ? 0f : 0.9f);
        using var node = CreateNode(threshold: -100f, attack: 50f, hold: 0f, release: 100f, range: -60f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(sampleCount / (double)SampleRate)));

        int probeWidth = SampleRate / 1000;
        float onsetPeakDb = PeakDb(output, silenceSamples, probeWidth);
        Assert.That(onsetPeakDb, Is.LessThan(-20f),
            $"Digital silence at min threshold must keep the gate closed; loud onset should ramp from closed but measured {onsetPeakDb:F2} dB");
    }

    [Test]
    public void Process_BelowThresholdLeadIn_StartsAtRangeFloorNotFullMute()
    {
        const int sampleCount = SampleRate / 10;
        const float rangeDb = -20f;
        const float amplitude = 0.01f; // ≈-40 dB, below the -30 dB threshold
        using var input = CreateConstantBuffer(amplitude, sampleCount);
        using var node = CreateNode(threshold: -30f, attack: 100f, hold: 0f, release: 100f, range: rangeDb);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(sampleCount / (double)SampleRate)));

        float firstGainDb = 20f * MathF.Log10(MathF.Abs(output.GetChannelData(0)[0]) / amplitude);
        Assert.That(firstGainDb, Is.EqualTo(rangeDb).Within(2f),
            $"Below-threshold lead-in should start at the Range floor ({rangeDb} dB), but first-sample gain was " +
            $"{firstGainDb:F2} dB (a value near -100 dB indicates a fade-in from the reset sentinel).");
    }

    [TestCase(-1L)]
    [TestCase(0L)]
    [TestCase(1L)]
    public void Process_OneTickBoundaryRounding_DoesNotResetGate(long tickOffset)
    {
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = MediaTime.FromSeconds(chunkSamples / (double)SampleRate);
        var ctx1 = CreateContext(MediaTime.Zero, chunkDuration);
        var ctx2 = CreateContext(chunkDuration + MediaTime.FromTimeSpan(TimeSpan.FromTicks(tickOffset)), chunkDuration);

        using var node = CreateNode();
        using var warmupInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(warmupInput));
        using var warmup = node.Process(ctx1);
        node.ClearInputs();
        using var followInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(followInput));
        using var followOutput = node.Process(ctx2);

        using var nodeFresh = CreateNode();
        using var freshInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new BufferReplayNode(freshInput));
        using var freshOutput = nodeFresh.Process(ctx1);

        float continuingFirst = MathF.Abs(followOutput.GetChannelData(0)[0]);
        float freshFirst = MathF.Abs(freshOutput.GetChannelData(0)[0]);
        Assert.That(continuingFirst, Is.GreaterThan(freshFirst),
            $"A one-tick boundary rounding must not reset the gate (continuing≈{continuingFirst:F4}, fresh≈{freshFirst:F4}).");
    }

    [Test]
    public void Process_Hold_KeepsGateOpenForEveryConfiguredSample()
    {
        const int holdSamples = 1;
        const float holdMs = holdSamples * 1000f / SampleRate;
        const int loudSamples = SampleRate / 100;
        const int sampleCount = loudSamples + 8;
        using var input = CreateBuffer(1, sampleCount, (_, i) => i < loudSamples ? 0.9f : 0.003f);
        using var node = CreateNode(threshold: -40f, attack: 0.1f, hold: holdMs, release: 1f, range: -60f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(sampleCount / (double)SampleRate)));

        var data = output.GetChannelData(0);
        float heldGain = MathF.Abs(data[loudSamples]) / 0.003f;
        float releasedGain = MathF.Abs(data[loudSamples + 1]) / 0.003f;

        Assert.That(heldGain, Is.EqualTo(1f).Within(0.01f),
            $"A {holdSamples}-sample hold must keep the first below-threshold sample fully open, but its gain was {heldGain:F4}.");
        Assert.That(releasedGain, Is.LessThan(heldGain),
            $"The sample after the hold window must have started releasing (held≈{heldGain:F4}, released≈{releasedGain:F4}).");
    }

    [Test]
    public void Process_NonFinitePropertyDefault_FallsBackToDeclaredConstant()
    {
        const int sampleCount = SampleRate / 4;
        using var input = CreateConstantBuffer(0.9f, sampleCount);

        using var node = CreateNode(threshold: float.NaN, attack: float.NaN, hold: float.NaN, release: float.NaN, range: float.NaN);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.25)));

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
        Assert.That(steadyPeakDb, Is.EqualTo(PeakDb(input, 0)).Within(1f),
            $"With non-finite property defaults the declared constants must apply, but the tail measured {steadyPeakDb:F2} dB.");
    }

    [Test]
    public void Process_LinkedSurround_OpensEveryChannelFromLoudestChannel()
    {
        const int sampleCount = SampleRate / 2;
        const int channels = 4;
        const float loud = 0.9f;
        const float quiet = 0.0005f; // ≈-66 dB, below the -40 dB threshold
        float[] amps = [quiet, quiet, loud, quiet];
        using var input = CreateBuffer(channels, sampleCount, (ch, _) => amps[ch]);

        using var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));
        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.5)));

        Assert.That(output.ChannelCount, Is.EqualTo(channels));

        int steadyStart = sampleCount / 2;
        Assert.That(PeakDb(output, steadyStart, channel: 2), Is.EqualTo(20f * MathF.Log10(loud)).Within(1f),
            "The loudest channel must open the gate and pass at unity.");

        float expectedQuietDb = 20f * MathF.Log10(quiet);
        foreach (int ch in new[] { 0, 1, 3 })
        {
            Assert.That(PeakDb(output, steadyStart, channel: ch), Is.EqualTo(expectedQuietDb).Within(1f),
                $"Channel {ch} must pass at unity because channel 2 holds the linked gate open.");
        }
    }

    [Test]
    public void Process_StaticAndAnimatedPaths_MatchForSurroundBuffer()
    {
        // Distinct channel levels expose detector or channel mix-ups in either surround path.
        const int loudSamples = SampleRate / 4;
        const int sampleCount = SampleRate / 2;
        const int channels = 4;
        var duration = MediaTime.FromSeconds(sampleCount / (double)SampleRate);
        using var input = CreateBuffer(
            channels, sampleCount,
            (ch, i) => ch == 2 ? (i < loudSamples ? 0.9f : 0.003f) : 0.0002f * (ch + 1));

        using var staticNode = CreateNode(threshold: -40f, attack: 2f, hold: 5f, release: 30f, range: -50f);
        staticNode.AddInput(new BufferReplayNode(input));
        using var staticOut = staticNode.Process(CreateContext(MediaTime.Zero, duration));

        using var animatedNode = CreateNode(attack: 2f, hold: 5f, release: 30f, range: -50f);
        animatedNode.Threshold.SetKeyFrameAnimation(Ramp(-40f, -40f, duration));
        animatedNode.AddInput(new BufferReplayNode(input));
        using var animatedOut = animatedNode.Process(CreateContext(MediaTime.Zero, duration));

        Assert.That(animatedOut.ChannelCount, Is.EqualTo(channels));
        for (int ch = 0; ch < channels; ch++)
        {
            var s = staticOut.GetChannelData(ch);
            var a = animatedOut.GetChannelData(ch);
            for (int i = 0; i < sampleCount; i++)
            {
                Assert.That(a[i], Is.EqualTo(s[i]).Within(1e-4f),
                    $"Static and animated surround paths diverged at [{ch}][{i}]: static={s[i]}, animated={a[i]}");
            }
        }
    }

    [Test]
    public void Process_AnimatedOutOfRangeParameter_LogsClampWarningOncePerParameter()
    {
        const int sampleCount = SampleRate / 4;
        using var input = CreateConstantBuffer(0.9f, sampleCount);

        using var node = CreateNode();
        node.Attack.SetKeyFrameAnimation(Ramp(1e9f, 1e9f, MediaTime.FromSeconds(0.25)));
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(MediaTime.Zero, MediaTime.FromSeconds(0.25)));

        Assert.That(node.ClampWarnings, Is.EqualTo(1),
            "An out-of-range animated Attack must warn exactly once for the whole chunk.");
    }

    [Test]
    public void Process_ZeroHold_ModulatesLowFrequencyGainAcrossZeroCrossings()
    {
        // With an unsmoothed detector, zero Hold and fast Release pump near zero crossings.
        const int sampleCount = SampleRate / 4;
        const float amplitude = 0.1f; // ≈-20 dB, well above the -40 dB threshold
        const float thresholdLinear = 0.01f; // the -40 dB threshold in linear terms
        var duration = MediaTime.FromSeconds(sampleCount / (double)SampleRate);
        using var input = CreateSineBuffer(amplitude, 50f, sampleCount, channels: 1);

        using var fastNode = CreateNode(threshold: -40f, attack: 0.1f, hold: MinHoldMs, release: MinReleaseMs, range: -60f);
        fastNode.AddInput(new BufferReplayNode(input));
        using var fastOut = fastNode.Process(CreateContext(MediaTime.Zero, duration));

        using var defaultNode = CreateNode(
            threshold: DefaultThresholdDb, attack: DefaultAttackMs, hold: DefaultHoldMs,
            release: DefaultReleaseMs, range: DefaultRangeDb);
        defaultNode.AddInput(new BufferReplayNode(input));
        using var defaultOut = defaultNode.Process(CreateContext(MediaTime.Zero, duration));

        int steadyStart = sampleCount / 2;
        float fastMinGain = MinGainWhereInputIsAudible(input, fastOut, steadyStart, thresholdLinear);
        float defaultMinGain = MinGainWhereInputIsAudible(input, defaultOut, steadyStart, thresholdLinear);

        Assert.That(fastMinGain, Is.LessThan(0.2f),
            $"Hold=0 with the fastest Release is expected to pump the gain near zero crossings, but the " +
            $"minimum gain was {fastMinGain:F4} — if this rose, detector smoothing changed.");
        Assert.That(defaultMinGain, Is.GreaterThan(0.99f),
            $"The default Hold/Release must hold the gate fully open across zero crossings, but the " +
            $"minimum gain was {defaultMinGain:F4}.");
    }
}
