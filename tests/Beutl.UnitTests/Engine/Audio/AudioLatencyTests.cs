using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using Moq;

using static Beutl.UnitTests.Engine.Audio.AudioTestBuffers;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioLatencyTests
{
    private const int SampleRate = 48000;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (Log.LoggerFactory is null)
        {
            Log.LoggerFactory = LoggerFactory.Create(_ => { });
        }
    }

    private static int ExpectedLookaheadSamples(float lookaheadMs, int sampleRate)
        => (int)(lookaheadMs / 1000f * sampleRate);

    private static LimiterNode CreateLimiterNode(float lookaheadMs)
        => new()
        {
            Threshold = Property.CreateAnimatable(LimiterParameters.DefaultThresholdDb),
            Release = Property.CreateAnimatable(LimiterParameters.DefaultReleaseMs),
            Lookahead = Property.CreateAnimatable(lookaheadMs),
            MakeupGain = Property.CreateAnimatable(LimiterParameters.DefaultMakeupGainDb),
        };

    private static LimiterEffect CreateLimiterEffect(float lookaheadMs)
    {
        var effect = new LimiterEffect();
        effect.Lookahead.CurrentValue = lookaheadMs;
        return effect;
    }

    private static AudioProcessContext CreateContext(int sampleCount, int sampleRate = SampleRate)
    {
        var duration = TimeSpan.FromSeconds((double)sampleCount / sampleRate);
        return new AudioProcessContext(new TimeRange(TimeSpan.Zero, duration), sampleRate, new AnimationSampler(), null);
    }

    [TestCase(5f)]
    [TestCase(10f)]
    public void Process_DelaysImpulse_ByLookaheadSamples(float lookaheadMs)
    {
        const int sampleCount = 4096;
        int expected = ExpectedLookaheadSamples(lookaheadMs, SampleRate);

        using var input = CreateBuffer(2, sampleCount, (_, i) => i == 0 ? 0.5f : 0f);

        using var node = CreateLimiterNode(lookaheadMs);
        node.AddInput(new BufferReplayNode(input));

        using var output = node.Process(CreateContext(sampleCount));

        var data = output.GetChannelData(0);
        int peakIndex = 0;
        float peak = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            float abs = MathF.Abs(data[i]);
            if (abs > peak)
            {
                peak = abs;
                peakIndex = i;
            }
        }

        Assert.That(peakIndex, Is.EqualTo(expected),
            "The impulse should emerge delayed by exactly the lookahead samples.");
    }

    [TestCase(0f, true)]
    [TestCase(5f, false)]
    [TestCase(20f, false)]
    public void LimiterNode_GetLatencySamples_MatchesLookahead_At48k(float lookaheadMs, bool expectedZero)
    {
        using var node = CreateLimiterNode(lookaheadMs);

        int latency = node.GetLatencySamples(SampleRate);

        Assert.That(latency, Is.EqualTo(ExpectedLookaheadSamples(lookaheadMs, SampleRate)));
        Assert.That(latency == 0, Is.EqualTo(expectedZero));
    }

    [TestCase(5f)]
    [TestCase(20f)]
    public void LimiterNode_GetLatencySamples_ScalesWithSampleRate(float lookaheadMs)
    {
        using var node = CreateLimiterNode(lookaheadMs);

        int at48k = node.GetLatencySamples(48000);
        int at96k = node.GetLatencySamples(96000);

        Assert.That(at48k, Is.EqualTo(ExpectedLookaheadSamples(lookaheadMs, 48000)));
        Assert.That(at96k, Is.EqualTo(ExpectedLookaheadSamples(lookaheadMs, 96000)));
        Assert.That(at96k, Is.EqualTo(at48k * 2), "Doubling the sample rate doubles the sample latency.");
    }

    [Test]
    public void LimiterNode_GetLatencySamples_QueryableBeforeProcess()
    {
        using var node = CreateLimiterNode(5f);

        Assert.That(node.GetLatencySamples(SampleRate), Is.EqualTo(ExpectedLookaheadSamples(5f, SampleRate)));
    }

    [TestCase(5f)]
    [TestCase(20f)]
    public void LimiterNode_GetLatencySamples_MatchesActualDelay(float lookaheadMs)
    {
        const int sampleCount = 4096;
        using var input = CreateBuffer(2, sampleCount, (_, i) => i == 0 ? 0.5f : 0f);

        using var node = CreateLimiterNode(lookaheadMs);
        node.AddInput(new BufferReplayNode(input));

        int reported = node.GetLatencySamples(SampleRate);

        using var output = node.Process(CreateContext(sampleCount));
        var data = output.GetChannelData(0);
        int peakIndex = 0;
        float peak = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            float abs = MathF.Abs(data[i]);
            if (abs > peak)
            {
                peak = abs;
                peakIndex = i;
            }
        }

        Assert.That(reported, Is.EqualTo(peakIndex),
            "The reported latency must equal the delay Process actually applies.");
    }

    [Test]
    public void PassThroughNodes_GetLatencySamples_AreZero()
    {
        using var gain = new GainNode { Gain = Property.CreateAnimatable(100f) };
        Assert.That(gain.GetLatencySamples(SampleRate), Is.EqualTo(0));

        using var buffer = CreateConstantBuffer(0.1f, 16);
        using var replay = new BufferReplayNode(buffer);
        Assert.That(replay.GetLatencySamples(SampleRate), Is.EqualTo(0), "AudioNode default is zero latency.");
    }

    [TestCase(0)]
    [TestCase(200)]
    [TestCase(int.MaxValue)]
    public void Effects_GetLatencySamples_PassThroughInputLatency(int inputLatency)
    {
        var fallback = new FallbackAudioEffect();
        var compressor = new CompressorEffect();
        var equalizer = new EqualizerEffect();
        var group = new AudioEffectGroup();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fallback.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(inputLatency));
            Assert.That(compressor.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(inputLatency));
            Assert.That(equalizer.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(inputLatency));
            Assert.That(group.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(inputLatency));
        }
    }

    [TestCase(0, 240)]
    [TestCase(200, 440)]
    [TestCase(int.MaxValue - 100, int.MaxValue)]
    [TestCase(int.MaxValue, int.MaxValue)]
    public void LimiterEffect_GetLatencySamples_MatchesGraphWithInputLatency(int inputLatency, int expected)
    {
        var effect = CreateLimiterEffect(5f);
        using var context = new AudioContext(SampleRate, 2);
        AudioNode source = context.AddNode(new FixedLatencyNode(inputLatency));
        AudioNode node = effect.CreateNode(context, source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(effect.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(expected));
            Assert.That(node.GetTotalLatencySamples(SampleRate), Is.EqualTo(expected));
            Assert.That(node.GetLatencySamples(SampleRate), Is.EqualTo(240));
        }
    }

    [TestCase(0)]
    [TestCase(200)]
    [TestCase(int.MaxValue)]
    public void Effects_GetLatencySamples_DisabledPassThroughInputLatency(int inputLatency)
    {
        var fallback = new FallbackAudioEffect { IsEnabled = false };
        var effect = CreateLimiterEffect(5f);
        effect.IsEnabled = false;
        var stretch = new TimeStretchEffect { IsEnabled = false };
        stretch.Speed.CurrentValue = 50f;
        var group = new AudioEffectGroup { IsEnabled = false };
        group.Children.Add(new FixedLatencyEffect(-1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fallback.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(inputLatency));
            Assert.That(effect.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(inputLatency));
            Assert.That(stretch.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(inputLatency));
            Assert.That(group.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(inputLatency));
        }
    }

    [TestCase(0)]
    [TestCase(200)]
    public void AudioEffectGroup_GetLatencySamples_SumsEnabledChildren(int inputLatency)
    {
        var group = new AudioEffectGroup();
        group.Children.Add(CreateLimiterEffect(5f));
        group.Children.Add(CreateLimiterEffect(10f));

        int expected = inputLatency + ExpectedLookaheadSamples(5f, SampleRate) + ExpectedLookaheadSamples(10f, SampleRate);
        Assert.That(group.GetLatencySamples(SampleRate, inputLatency), Is.EqualTo(expected));
    }

    [Test]
    public void AudioEffectGroup_GetLatencySamples_ExcludesDisabledChildren()
    {
        var disabled = CreateLimiterEffect(10f);
        disabled.IsEnabled = false;

        var group = new AudioEffectGroup();
        group.Children.Add(CreateLimiterEffect(5f));
        group.Children.Add(disabled);
        group.Children.Add(new FixedLatencyEffect(-1) { IsEnabled = false });
        var disabledGroup = new AudioEffectGroup { IsEnabled = false };
        var stretch = new TimeStretchEffect();
        stretch.Speed.CurrentValue = 50f;
        disabledGroup.Children.Add(stretch);
        group.Children.Add(disabledGroup);
        using var context = new AudioContext(SampleRate, 2);
        AudioNode source = context.AddNode(new FixedLatencyNode(200));
        AudioNode output = group.CreateNode(context, source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(group.GetLatencySamples(SampleRate, 200), Is.EqualTo(200 + ExpectedLookaheadSamples(5f, SampleRate)));
            Assert.That(output.GetTotalLatencySamples(SampleRate), Is.EqualTo(group.GetLatencySamples(SampleRate, 200)));
        }
    }

    [Test]
    public void AudioEffectGroup_GetLatencySamples_DisabledGroupReportsZero()
    {
        var group = new AudioEffectGroup { IsEnabled = false };
        group.Children.Add(CreateLimiterEffect(5f));
        group.Children.Add(CreateLimiterEffect(10f));

        Assert.That(group.GetLatencySamples(SampleRate), Is.EqualTo(0));
    }

    [TestCase(int.MaxValue, 1)]
    [TestCase(int.MaxValue - 10, 20)]
    public void AudioEffectGroup_GetLatencySamples_SaturatesUnboundedOrOverflowingTotals(
        int firstLatency,
        int secondLatency)
    {
        var group = new AudioEffectGroup();
        group.Children.Add(new FixedLatencyEffect(firstLatency));
        group.Children.Add(new FixedLatencyEffect(secondLatency));

        Assert.That(group.GetLatencySamples(SampleRate), Is.EqualTo(int.MaxValue));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void AudioEffectGroup_GetLatencySamples_NegativeChildThrows(bool precededByUnbounded, bool followedByUnbounded)
    {
        var group = new AudioEffectGroup();
        if (precededByUnbounded)
        {
            group.Children.Add(new FixedLatencyEffect(int.MaxValue));
        }
        group.Children.Add(new FixedLatencyEffect(-1));
        if (followedByUnbounded)
        {
            group.Children.Add(new FixedLatencyEffect(int.MaxValue));
        }

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
            () => group.GetLatencySamples(SampleRate));
        Assert.That(exception!.Message, Does.Contain(nameof(FixedLatencyEffect)).And.Contain("-1"));
    }

    [Test]
    public void GetTotalLatencySamples_OverLinearCascade_Sums()
    {
        using var buffer = CreateConstantBuffer(0.1f, 16);
        using var source = new BufferReplayNode(buffer);
        using var first = CreateLimiterNode(5f);
        using var second = CreateLimiterNode(10f);
        first.AddInput(source);
        second.AddInput(first);

        int expected = ExpectedLookaheadSamples(5f, SampleRate) + ExpectedLookaheadSamples(10f, SampleRate);
        Assert.That(second.GetTotalLatencySamples(SampleRate), Is.EqualTo(expected));
        Assert.That(first.GetTotalLatencySamples(SampleRate), Is.EqualTo(ExpectedLookaheadSamples(5f, SampleRate)),
            "The leaf BufferReplayNode feeding the cascade contributes zero latency.");
    }

    [Test]
    public void GetTotalLatencySamples_OverFanIn_TakesMax()
    {
        using var bufferA = CreateConstantBuffer(0.1f, 16);
        using var bufferB = CreateConstantBuffer(0.1f, 16);
        using var branchA = CreateLimiterNode(5f);
        using var branchB = CreateLimiterNode(10f);
        branchA.AddInput(new BufferReplayNode(bufferA));
        branchB.AddInput(new BufferReplayNode(bufferB));

        using var mixer = new MixerNode();
        mixer.AddInput(branchA);
        mixer.AddInput(branchB);

        int slowest = ExpectedLookaheadSamples(10f, SampleRate);
        Assert.That(mixer.GetTotalLatencySamples(SampleRate), Is.EqualTo(slowest));
    }

    [Test]
    public void GetTotalLatencySamples_NegativeOwnLatencyThrows()
    {
        using var node = new FixedLatencyNode(-1);

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
            () => node.GetTotalLatencySamples(SampleRate));
        Assert.That(exception!.Message, Does.Contain(nameof(FixedLatencyNode)).And.Contain("-1"));
    }

    [Test]
    public void GetTotalLatencySamples_NegativeChildTotalThrows()
    {
        using var child = new FixedLatencyNode(-1, overrideTotal: true);
        using var parent = new FixedLatencyNode(0);
        parent.AddInput(child);

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
            () => parent.GetTotalLatencySamples(SampleRate));
        Assert.That(exception!.Message, Does.Contain(nameof(FixedLatencyNode)).And.Contain("-1"));
    }

    [Test]
    public void AddInput_ReentrantHookFailure_RestoresTheCompleteInputList()
    {
        using var node = new ReentrantInputNode();
        using var input = new FixedLatencyNode(0);

        Assert.Throws<InvalidOperationException>(() => node.AddInput(input));

        Assert.That(node.Inputs, Is.Empty,
            "A failed reentrant input hook must remove both the original and any auxiliary inputs it appended.");
    }

    [Test]
    public void SpeedNode_GetTotalLatencySamples_NegativeChildTotalThrows()
    {
        using var child = new FixedLatencyNode(-1, overrideTotal: true);
        using var speed = new SpeedNode { Speed = Property.CreateAnimatable(100f) };
        speed.AddInput(child);

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
            () => speed.GetTotalLatencySamples(SampleRate));
        Assert.That(exception!.Message, Does.Contain(nameof(FixedLatencyNode)).And.Contain("-1"));
    }

    [TestCase(50f)]
    [TestCase(100f)]
    [TestCase(200f)]
    public void SpeedNode_GetTotalLatencySamples_ConvertsUpstreamLatencyToOutputDomain(float speedPercent)
    {
        using var limiter = CreateLimiterNode(5f);
        using var speed = new SpeedNode { Speed = Property.CreateAnimatable(speedPercent) };
        speed.AddInput(limiter);

        int upstreamLatency = ExpectedLookaheadSamples(5f, SampleRate);
        int expected = (int)Math.Ceiling(upstreamLatency / (speedPercent / 100d));

        Assert.That(speed.GetTotalLatencySamples(SampleRate), Is.EqualTo(expected));
    }

    [Test]
    public void SpeedNode_GetTotalLatencySamples_AnimatedSpeedUsesSlowestKeyFrame()
    {
        var speedProperty = Property.CreateAnimatable(200f);
        speedProperty.Animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 200f },
                new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 50f },
            },
        };
        using var limiter = CreateLimiterNode(5f);
        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(limiter);

        int upstreamLatency = ExpectedLookaheadSamples(5f, SampleRate);

        Assert.That(speed.GetTotalLatencySamples(SampleRate), Is.EqualTo(upstreamLatency * 2));
    }

    [Test]
    public void SpeedNode_GetDrainLatencySamples_UsesTerminalAnimatedSpeed()
    {
        const int processSamples = SampleRate;
        var duration = AudioProcessContext.GetDurationForSampleCount(processSamples, SampleRate);
        var terminalSample = AudioProcessContext.GetDurationForSampleCount(1, SampleRate);
        var speedProperty = Property.CreateAnimatable(100f);
        speedProperty.Animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 50f },
                new KeyFrame<float> { KeyTime = duration - terminalSample - terminalSample, Value = 100f },
            },
        };

        using var limiter = CreateLimiterNode(5f);
        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(limiter);
        using var input = CreateConstantBuffer(0.1f, processSamples);
        limiter.AddInput(new BufferReplayNode(input));

        using var processed = speed.Process(CreateContext(processSamples));

        int upstreamLatency = ExpectedLookaheadSamples(5f, SampleRate);
        Assert.Multiple(() =>
        {
            Assert.That(speed.GetTotalLatencySamples(SampleRate), Is.EqualTo(upstreamLatency * 2),
                "The public report must remain conservative for the slowest animated speed.");
            Assert.That(speed.GetDrainLatencySamples(SampleRate), Is.EqualTo(upstreamLatency),
                "The drain report must use the speed latched at the terminal sample.");
        });
    }

    [Test]
    public void SpeedNode_GetTotalLatencySamples_BoundedCustomAnimationUsesMinimum()
    {
        var speedProperty = Property.CreateAnimatable(100f);
        var animation = new Mock<IAnimationRange<float>>();
        float minimum = 50f;
        float maximum = 200f;
        animation.Setup(x => x.TryGetOutputRange(out minimum, out maximum)).Returns(true);
        speedProperty.Animation = animation.Object;

        using var limiter = CreateLimiterNode(5f);
        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(limiter);

        int upstreamLatency = ExpectedLookaheadSamples(5f, SampleRate);

        Assert.That(speed.GetTotalLatencySamples(SampleRate), Is.EqualTo(upstreamLatency * 2));
    }

    [Test]
    public void AnimationRangeExtensions_UsesCustomProviderThroughIAnimationContract()
    {
        var animation = new Mock<IAnimationRange<float>>();
        float minimum = 50f;
        float maximum = 200f;
        animation.Setup(x => x.TryGetOutputRange(out minimum, out maximum)).Returns(true);

        IAnimation<float> source = animation.Object;

        Assert.That(source.TryGetOutputRange(out float actualMinimum, out float actualMaximum), Is.True);
        Assert.That(actualMinimum, Is.EqualTo(minimum));
        Assert.That(actualMaximum, Is.EqualTo(maximum));
    }

    [Test]
    public void SpeedNode_ProcessAndFlush_BoundedCustomAnimationUsesAnimationValues()
    {
        var speedProperty = Property.CreateAnimatable(100f);
        var animation = new Mock<IAnimationRange<float>>();
        float minimum = 50f;
        float maximum = 200f;
        animation.Setup(x => x.TryGetOutputRange(out minimum, out maximum)).Returns(true);
        animation.SetupGet(x => x.UseGlobalClock).Returns(false);
        animation.Setup(x => x.GetAnimatedValue(It.IsAny<TimeSpan>())).Returns(100f);
        animation.Setup(x => x.Interpolate(It.IsAny<TimeSpan>())).Returns(100f);
        speedProperty.Animation = animation.Object;

        using var input = CreateConstantBuffer(0.1f, 16);
        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(new BufferReplayNode(input));

        using AudioBuffer output = speed.Process(CreateContext(16));
        using AudioBuffer tail = speed.Flush(CreateContext(16));

        Assert.Multiple(() =>
        {
            Assert.That(output.SampleCount, Is.EqualTo(16));
            Assert.That(tail.SampleCount, Is.EqualTo(16));
        });
    }

    [Test]
    public void SpeedNode_GetTotalLatencySamples_BackEaseUsesItsOvershootBound()
    {
        var easing = new BackEaseIn();
        var speedProperty = Property.CreateAnimatable(100f);
        speedProperty.Animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100f },
                new KeyFrame<float>
                {
                    KeyTime = TimeSpan.FromSeconds(1),
                    Value = 200f,
                    Easing = easing,
                },
            },
        };
        using var limiter = CreateLimiterNode(5f);
        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(limiter);

        float sampledMinimum = float.PositiveInfinity;
        for (int i = 0; i <= 1000; i++)
        {
            float progress = i / 1000f;
            sampledMinimum = Math.Min(sampledMinimum, 100f + easing.Ease(progress) * 100f);
        }

        int upstreamLatency = ExpectedLookaheadSamples(5f, SampleRate);
        int requiredForObservedOvershoot = (int)Math.Ceiling(upstreamLatency / (sampledMinimum / 100d));

        Assert.That(speed.GetTotalLatencySamples(SampleRate), Is.GreaterThanOrEqualTo(requiredForObservedOvershoot));
    }

    [Test]
    public void SpeedNode_GetTotalLatencySamples_UnknownEasingSaturates()
    {
        var speedProperty = Property.CreateAnimatable(100f);
        speedProperty.Animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100f },
                new KeyFrame<float>
                {
                    KeyTime = TimeSpan.FromSeconds(1),
                    Value = 200f,
                    Easing = new UnknownRangeEasing(),
                },
            },
        };
        using var limiter = CreateLimiterNode(5f);
        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(limiter);

        Assert.That(speed.GetTotalLatencySamples(SampleRate), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void SpeedNode_GetTotalLatencySamples_UnknownAnimationSaturates()
    {
        var speedProperty = Property.CreateAnimatable(100f);
        speedProperty.Animation = new Mock<IAnimation<float>>().Object;
        using var limiter = CreateLimiterNode(5f);
        using var speed = new SpeedNode { Speed = speedProperty };
        speed.AddInput(limiter);

        Assert.That(speed.GetTotalLatencySamples(SampleRate), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void SpeedNode_GetTotalLatencySamples_StoppedSpeedSaturates()
    {
        using var limiter = CreateLimiterNode(5f);
        using var speed = new SpeedNode { Speed = Property.CreateAnimatable(0f) };
        speed.AddInput(limiter);

        Assert.That(speed.GetTotalLatencySamples(SampleRate), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void GetTotalLatencySamples_DownstreamLatencyPreservesUpstreamSaturation()
    {
        using var upstreamLimiter = CreateLimiterNode(5f);
        using var stopped = new SpeedNode { Speed = Property.CreateAnimatable(0f) };
        using var downstreamLimiter = CreateLimiterNode(5f);
        stopped.AddInput(upstreamLimiter);
        downstreamLimiter.AddInput(stopped);

        Assert.That(downstreamLimiter.GetTotalLatencySamples(SampleRate), Is.EqualTo(int.MaxValue));
    }

    [TestCase(0, true, 0)]
    [TestCase(-1, true, 200)]
    [TestCase(0, true, int.MaxValue)]
    [TestCase(0, false, 0)]
    [TestCase(-1, false, 200)]
    [TestCase(0, false, int.MaxValue)]
    public void GetLatencySamples_NonPositiveSampleRate_Throws(int sampleRate, bool enabled, int inputLatency)
    {
        using var gain = new GainNode { Gain = Property.CreateAnimatable(100f) };
        using var limiterNode = CreateLimiterNode(5f);
        var limiterEffect = CreateLimiterEffect(5f);
        limiterEffect.IsEnabled = enabled;
        var fallback = new FallbackAudioEffect { IsEnabled = enabled };
        var stretch = new TimeStretchEffect { IsEnabled = enabled };
        var group = new AudioEffectGroup { IsEnabled = enabled };

        Assert.Throws<ArgumentOutOfRangeException>(() => gain.GetLatencySamples(sampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => limiterNode.GetLatencySamples(sampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => fallback.GetLatencySamples(sampleRate, inputLatency));
        Assert.Throws<ArgumentOutOfRangeException>(() => limiterEffect.GetLatencySamples(sampleRate, inputLatency));
        Assert.Throws<ArgumentOutOfRangeException>(() => stretch.GetLatencySamples(sampleRate, inputLatency));
        Assert.Throws<ArgumentOutOfRangeException>(() => group.GetLatencySamples(sampleRate, inputLatency));

        Assert.Throws<ArgumentOutOfRangeException>(() => gain.GetTotalLatencySamples(sampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => limiterNode.GetTotalLatencySamples(sampleRate));
        using var speed = new SpeedNode { Speed = Property.CreateAnimatable(50f) };
        Assert.Throws<ArgumentOutOfRangeException>(() => speed.GetTotalLatencySamples(sampleRate));
    }

    [TestCase(-1, true)]
    [TestCase(int.MinValue, true)]
    [TestCase(-1, false)]
    [TestCase(int.MinValue, false)]
    public void Effects_GetLatencySamples_NegativeInputLatency_Throws(int inputLatency, bool enabled)
    {
        var fallback = new FallbackAudioEffect { IsEnabled = enabled };
        var limiter = new LimiterEffect { IsEnabled = enabled };
        var stretch = new TimeStretchEffect { IsEnabled = enabled };
        var group = new AudioEffectGroup { IsEnabled = enabled };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => fallback.GetLatencySamples(SampleRate, inputLatency),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("inputLatency"));
            Assert.That(() => limiter.GetLatencySamples(SampleRate, inputLatency),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("inputLatency"));
            Assert.That(() => stretch.GetLatencySamples(SampleRate, inputLatency),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("inputLatency"));
            Assert.That(() => group.GetLatencySamples(SampleRate, inputLatency),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("inputLatency"));
        }
    }

    private sealed class UnknownRangeEasing : Easing
    {
        public override float Ease(float progress) => progress;
    }

}

internal sealed partial class FixedLatencyEffect(int latencySamples) : AudioEffect
{
    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode) => inputNode;

    public override int GetLatencySamples(int sampleRate, int inputLatency = 0)
    {
        base.GetLatencySamples(sampleRate, inputLatency);
        if (!IsEnabled)
            return inputLatency;

        // Deliberately invalid reports exercise the group's validation of external overrides.
        return latencySamples < 0 ? latencySamples : AudioLatency.SaturatingAdd(inputLatency, latencySamples);
    }
}

internal sealed class FixedLatencyNode(int latencySamples, bool overrideTotal = false) : AudioNode
{
    public override AudioBuffer Process(AudioProcessContext context)
        => new(context.SampleRate, 2, context.GetSampleCount());

    public override int GetLatencySamples(int sampleRate) => latencySamples;

    public override int GetTotalLatencySamples(int sampleRate)
        => overrideTotal ? latencySamples : base.GetTotalLatencySamples(sampleRate);
}

internal sealed class ReentrantInputNode : AudioNode
{
    private bool _nested;

    public override AudioBuffer Process(AudioProcessContext context)
        => new(context.SampleRate, 2, context.GetSampleCount());

    protected override void OnInputAdded(AudioNode input, int index)
    {
        if (!_nested)
        {
            _nested = true;
            AddInput(new FixedLatencyNode(0));
            throw new InvalidOperationException("The input hook failed after a reentrant mutation.");
        }
    }
}
