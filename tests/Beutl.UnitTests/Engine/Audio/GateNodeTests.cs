using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

using static Beutl.Audio.Effects.GateParameters;
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

    private static float ChannelPeakDb(AudioBuffer buffer, int channel, int startSample)
    {
        var data = buffer.GetChannelData(channel);
        float peak = 0f;
        for (int i = startSample; i < buffer.SampleCount; i++)
        {
            float a = MathF.Abs(data[i]);
            if (a > peak) peak = a;
        }
        return peak > 0f ? 20f * MathF.Log10(peak) : -100f;
    }

    private static AudioBuffer MakeInfinityHeadBuffer(int sampleCount, int sampleRate = SampleRate)
    {
        var buffer = CreateConstantBuffer(0.9f, sampleCount, 2, sampleRate);
        for (int ch = 0; ch < buffer.ChannelCount; ch++)
        {
            buffer.GetChannelData(ch)[0] = float.PositiveInfinity;
        }
        return buffer;
    }

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

    private static AudioBuffer MakeTwoPhaseBuffer(float loud, float quiet, int loudSamples, int totalSamples)
    {
        return CreateBuffer(2, totalSamples, (_, i) => i < loudSamples ? loud : quiet);
    }

    [Test]
    public void Process_SilenceInput_ProducesExactSilenceOutput()
    {
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
        // After one attack time constant, gain covers 1 - 1/e of the distance to 0 dB.
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

        int probeWidth = SampleRate / 25;
        float holdTailDb = PeakDbInWindow(holdOut, total - probeWidth, probeWidth);
        float noHoldTailDb = PeakDbInWindow(noHoldOut, total - probeWidth, probeWidth);

        Assert.That(holdTailDb, Is.GreaterThan(noHoldTailDb + 20f),
            $"Hold should keep the gate open over the quiet tail (hold≈{holdTailDb:F2} dB, no-hold≈{noHoldTailDb:F2} dB)");
    }

    [Test]
    public void Process_GateStateContinuesAcrossChunks()
    {
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
        const int silenceSamples = SampleRate / 5; // 200 ms
        const int sampleCount = SampleRate / 2;
        using var input = CreateBuffer(1, sampleCount, (_, i) => i < silenceSamples ? 0f : 0.9f);
        var node = CreateNode(threshold: -100f, attack: 50f, hold: 0f, release: 100f, range: -60f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate)));

        int probeWidth = SampleRate / 1000;
        float onsetPeakDb = PeakDbInWindow(output, silenceSamples, probeWidth);
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
        var node = CreateNode(threshold: -30f, attack: 100f, hold: 0f, release: 100f, range: rangeDb);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate)));

        float firstGainDb = 20f * MathF.Log10(MathF.Abs(output.GetChannelData(0)[0]) / amplitude);
        Assert.That(firstGainDb, Is.EqualTo(rangeDb).Within(2f),
            $"Below-threshold lead-in should start at the Range floor ({rangeDb} dB), but first-sample gain was " +
            $"{firstGainDb:F2} dB (a value near -100 dB indicates a fade-in from the reset sentinel).");
    }

    [TestCase(-1L)]
    [TestCase(1L)]
    public void Process_OneTickBoundaryRounding_DoesNotResetGate(long tickOffset)
    {
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);
        var ctx1 = CreateContext(TimeSpan.Zero, chunkDuration);
        var ctx2 = CreateContext(chunkDuration + TimeSpan.FromTicks(tickOffset), chunkDuration);

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

    [Test]
    public void Process_Hold_KeepsGateOpenForEveryConfiguredSample()
    {
        const int holdSamples = 1;
        const float holdMs = holdSamples * 1000f / SampleRate;
        const int loudSamples = SampleRate / 100;
        const int sampleCount = loudSamples + 8;
        using var input = CreateBuffer(1, sampleCount, (_, i) => i < loudSamples ? 0.9f : 0.003f);
        var node = CreateNode(threshold: -40f, attack: 0.1f, hold: holdMs, release: 1f, range: -60f);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(sampleCount / (double)SampleRate)));

        var data = output.GetChannelData(0);
        float heldGain = MathF.Abs(data[loudSamples]) / 0.003f;
        float releasedGain = MathF.Abs(data[loudSamples + 1]) / 0.003f;

        Assert.That(heldGain, Is.EqualTo(1f).Within(0.01f),
            $"A {holdSamples}-sample hold must keep the first below-threshold sample fully open, but its gain was {heldGain:F4}.");
        Assert.That(releasedGain, Is.LessThan(heldGain),
            $"The sample after the hold window must have started releasing (held≈{heldGain:F4}, released≈{releasedGain:F4}).");
    }

    [Test]
    public void Process_InputSampleRateMismatch_Throws()
    {
        const int altSampleRate = 44100;
        using var input = CreateConstantBuffer(0.9f, altSampleRate / 10, 2, altSampleRate);
        var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));

        Assert.Throws<InvalidOperationException>(
            () => node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.1))));
    }

    [Test]
    public void Process_NonFinitePropertyDefault_FallsBackToDeclaredConstant()
    {
        const int sampleCount = SampleRate / 4;
        using var input = CreateConstantBuffer(0.9f, sampleCount);

        var node = new GateNode
        {
            Threshold = Property.CreateAnimatable(float.NaN),
            Attack = Property.CreateAnimatable(float.NaN),
            Hold = Property.CreateAnimatable(float.NaN),
            Release = Property.CreateAnimatable(float.NaN),
            Range = Property.CreateAnimatable(float.NaN)
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
        Assert.That(steadyPeakDb, Is.EqualTo(PeakDb(input, 0)).Within(1f),
            $"With non-finite property defaults the declared constants must apply, but the tail measured {steadyPeakDb:F2} dB.");
    }

    [Test]
    public void Process_LinkedSurround_OpensEveryChannelFromLoudestChannel()
    {
        // Only channel 2 is above the threshold, so a detector that stops at channel 1 closes the gate.
        const int sampleCount = SampleRate / 2;
        const int channels = 4;
        const float loud = 0.9f;
        const float quiet = 0.0005f; // ≈-66 dB, below the -40 dB threshold
        float[] amps = [quiet, quiet, loud, quiet];
        using var input = CreateBuffer(channels, sampleCount, (ch, _) => amps[ch]);

        var node = CreateNode();
        node.AddInput(new BufferReplayNode(input));
        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)));

        Assert.That(output.ChannelCount, Is.EqualTo(channels));

        int steadyStart = sampleCount / 2;
        Assert.That(ChannelPeakDb(output, 2, steadyStart), Is.EqualTo(20f * MathF.Log10(loud)).Within(1f),
            "The loudest channel must open the gate and pass at unity.");

        float expectedQuietDb = 20f * MathF.Log10(quiet);
        foreach (int ch in new[] { 0, 1, 3 })
        {
            Assert.That(ChannelPeakDb(output, ch, steadyStart), Is.EqualTo(expectedQuietDb).Within(1f),
                $"Channel {ch} must pass at unity because channel 2 holds the linked gate open.");
        }
    }

    [Test]
    public void Process_StaticAndAnimatedPaths_MatchForSurroundBuffer()
    {
        // The >2-channel fallback is written out separately in each path. Only channel 2 crosses the
        // threshold and every channel carries a distinct level, so a detector or channel mix-up in
        // either loop diverges here.
        const int loudSamples = SampleRate / 4;
        const int sampleCount = SampleRate / 2;
        const int channels = 4;
        var duration = TimeSpan.FromSeconds(sampleCount / (double)SampleRate);
        using var input = CreateBuffer(
            channels, sampleCount,
            (ch, i) => ch == 2 ? (i < loudSamples ? 0.9f : 0.003f) : 0.0002f * (ch + 1));

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
    public void Process_NonFiniteSampleLatch_SurvivesSeekDiscontinuity()
    {
        // A seek resets DSP state only; re-arming here would re-log a persistent fault on every scrub.
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);
        var node = CreateNode();

        using var first = MakeInfinityHeadBuffer(chunkSamples);
        node.AddInput(new BufferReplayNode(first));
        using var firstOut = node.Process(CreateContext(TimeSpan.Zero, chunkDuration));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(1),
            "The first non-finite sample must emit exactly one warning.");

        node.ClearInputs();
        using var second = MakeInfinityHeadBuffer(chunkSamples);
        node.AddInput(new BufferReplayNode(second));
        using var seekedOut = node.Process(CreateContext(TimeSpan.FromSeconds(5.0), chunkDuration));
        Assert.That(node.NonFiniteSampleWarnings, Is.EqualTo(1),
            "A seek discontinuity must not re-arm the latch, so no second warning should be emitted.");
    }

    [Test]
    public void Process_NonFiniteSampleLatch_ReArmsOnSampleRateChange()
    {
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);
        var node = CreateNode();

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
            "A sample-rate change is a session boundary, so the recurring fault must warn again.");
    }

    [Test]
    public void Reset_ReArmsNonFiniteSampleLatch()
    {
        // The second chunk stays time-contiguous so only the explicit Reset() can re-arm the latch.
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);
        var node = CreateNode();

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
    public void Process_AnimatedOutOfRangeParameter_LogsClampWarningOncePerParameter()
    {
        // Not zero times (a hidden misconfiguration) and not per sample (audio-thread spam).
        const int sampleCount = SampleRate / 4;
        using var input = CreateConstantBuffer(0.9f, sampleCount);

        var attackAnim = new KeyFrameAnimation<float>();
        attackAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 1e9f, KeyTime = TimeSpan.Zero });
        attackAnim.KeyFrames.Add(new KeyFrame<float> { Easing = new LinearEasing(), Value = 1e9f, KeyTime = TimeSpan.FromSeconds(0.25) });
        var attackProperty = Property.CreateAnimatable(1f);
        attackProperty.Animation = attackAnim;

        var node = new GateNode
        {
            Threshold = Property.CreateAnimatable(-40f),
            Attack = attackProperty,
            Hold = Property.CreateAnimatable(10f),
            Release = Property.CreateAnimatable(50f),
            Range = Property.CreateAnimatable(-60f)
        };
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(0.25)));

        Assert.That(node.ClampWarnings, Is.EqualTo(1),
            "An out-of-range animated Attack must warn exactly once for the whole chunk.");
    }

    [Test]
    public void Process_EmptyChunkWithDuration_DoesNotMaskLaterDiscontinuity()
    {
        // A chunk that spans time but carries no samples leaves a hole; the chunk after it is not
        // contiguous with the chunk before it.
        const int chunkSamples = SampleRate / 10;
        var chunkDuration = TimeSpan.FromSeconds(chunkSamples / (double)SampleRate);

        var node = CreateNode();
        using var warmupInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(warmupInput));
        using var warmup = node.Process(CreateContext(TimeSpan.Zero, chunkDuration));

        node.ClearInputs();
        using var emptyInput = new AudioBuffer(SampleRate, 2, 0);
        node.AddInput(new BufferReplayNode(emptyInput));
        using var emptyOutput = node.Process(CreateContext(chunkDuration, chunkDuration));
        Assert.That(emptyOutput.SampleCount, Is.EqualTo(0));

        node.ClearInputs();
        using var afterHoleInput = CreateConstantBuffer(0.9f, chunkSamples);
        node.AddInput(new BufferReplayNode(afterHoleInput));
        using var afterHoleOutput = node.Process(CreateContext(chunkDuration + chunkDuration, chunkDuration));

        var nodeFresh = CreateNode();
        using var freshInput = CreateConstantBuffer(0.9f, chunkSamples);
        nodeFresh.AddInput(new BufferReplayNode(freshInput));
        using var freshOutput = nodeFresh.Process(CreateContext(TimeSpan.Zero, chunkDuration));

        Assert.That(
            MathF.Abs(afterHoleOutput.GetChannelData(0)[0]),
            Is.EqualTo(MathF.Abs(freshOutput.GetChannelData(0)[0])).Within(1e-4f),
            "The chunk after an empty-but-timed chunk must start from a reset gate, not the warmed-open one.");
    }

    [Test]
    public void Process_ZeroHold_ModulatesLowFrequencyGainAcrossZeroCrossings()
    {
        // Characterizes the unsmoothed peak detector: Hold bridges a waveform's zero crossings, so at
        // Hold = 0 with the fastest Release a tone that never leaves the open region still pumps.
        const int sampleCount = SampleRate / 4;
        const float amplitude = 0.1f; // ≈-20 dB, well above the -40 dB threshold
        const float thresholdLinear = 0.01f; // the -40 dB threshold in linear terms
        var duration = TimeSpan.FromSeconds(sampleCount / (double)SampleRate);
        using var input = CreateSineBuffer(amplitude, 50f, sampleCount, channels: 1);

        var fastNode = CreateNode(threshold: -40f, attack: 0.1f, hold: MinHoldMs, release: MinReleaseMs, range: -60f);
        fastNode.AddInput(new BufferReplayNode(input));
        using var fastOut = fastNode.Process(CreateContext(TimeSpan.Zero, duration));

        var defaultNode = CreateNode(
            threshold: DefaultThresholdDb, attack: DefaultAttackMs, hold: DefaultHoldMs,
            release: DefaultReleaseMs, range: DefaultRangeDb);
        defaultNode.AddInput(new BufferReplayNode(input));
        using var defaultOut = defaultNode.Process(CreateContext(TimeSpan.Zero, duration));

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
