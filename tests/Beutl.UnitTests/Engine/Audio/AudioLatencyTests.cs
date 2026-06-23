using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

using static Beutl.UnitTests.Engine.Audio.AudioTestBuffers;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioLatencyTests
{
    private const int SampleRate = 48000;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // LimiterNode touches Log on construction/processing; Log.LoggerFactory is write-once (??=).
        if (Log.LoggerFactory is null)
        {
            Log.LoggerFactory = LoggerFactory.Create(_ => { });
        }
    }

    // Reference math the limiter uses to convert a lookahead time to samples (unclamped; every value
    // exercised here is within the 0..20 ms range, where it matches LimiterNode's clamped result).
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

    // Baseline characterization: drives the real Process path and measures the delay an impulse incurs,
    // pinning the ground-truth latency the reporting API must reproduce. Runs green against unmodified
    // code, mirroring LimiterNodeTests.Process_LookaheadDelay_IsAccurate.
    [TestCase(5f)]
    [TestCase(10f)]
    public void Process_DelaysImpulse_ByLookaheadSamples(float lookaheadMs)
    {
        const int sampleCount = 4096;
        int expected = ExpectedLookaheadSamples(lookaheadMs, SampleRate);

        // An isolated impulse stays below the limiter threshold, so it passes through delayed but
        // un-attenuated; its peak position is the applied delay.
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
        // A freshly constructed node has never initialized its delay-line buffers; the report must not
        // depend on _maxLookaheadSamples being set by a prior Process call.
        using var node = CreateLimiterNode(5f);

        Assert.That(node.GetLatencySamples(SampleRate), Is.EqualTo(ExpectedLookaheadSamples(5f, SampleRate)));
    }

    // 20 ms is the MaxLookaheadMs boundary where ToLatencySamples and LimiterNode.Derive both clamp;
    // driving Process there confirms the report still equals the delay actually applied.
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

    [Test]
    public void Effects_GetLatencySamples_ZeroForNonLatencyEffects()
    {
        var compressor = new CompressorEffect();
        var equalizer = new EqualizerEffect();

        Assert.That(compressor.GetLatencySamples(SampleRate), Is.EqualTo(0));
        Assert.That(equalizer.GetLatencySamples(SampleRate), Is.EqualTo(0));
    }

    [Test]
    public void LimiterEffect_GetLatencySamples_MatchesNode()
    {
        var effect = CreateLimiterEffect(5f);
        using var node = CreateLimiterNode(5f);

        Assert.That(effect.GetLatencySamples(SampleRate), Is.EqualTo(node.GetLatencySamples(SampleRate)));
        Assert.That(effect.GetLatencySamples(SampleRate), Is.EqualTo(ExpectedLookaheadSamples(5f, SampleRate)));
    }

    [Test]
    public void LimiterEffect_GetLatencySamples_DisabledReportsZero()
    {
        var effect = CreateLimiterEffect(5f);
        effect.IsEnabled = false;

        Assert.That(effect.GetLatencySamples(SampleRate), Is.EqualTo(0));
    }

    [Test]
    public void AudioEffectGroup_GetLatencySamples_SumsEnabledChildren()
    {
        var group = new AudioEffectGroup();
        group.Children.Add(CreateLimiterEffect(5f));
        group.Children.Add(CreateLimiterEffect(10f));

        int expected = ExpectedLookaheadSamples(5f, SampleRate) + ExpectedLookaheadSamples(10f, SampleRate);
        Assert.That(group.GetLatencySamples(SampleRate), Is.EqualTo(expected));
    }

    [Test]
    public void AudioEffectGroup_GetLatencySamples_ExcludesDisabledChildren()
    {
        var disabled = CreateLimiterEffect(10f);
        disabled.IsEnabled = false;

        var group = new AudioEffectGroup();
        group.Children.Add(CreateLimiterEffect(5f));
        group.Children.Add(disabled);

        Assert.That(group.GetLatencySamples(SampleRate), Is.EqualTo(ExpectedLookaheadSamples(5f, SampleRate)));
    }

    [Test]
    public void AudioEffectGroup_GetLatencySamples_IgnoresGroupOwnIsEnabled()
    {
        // The group's own IsEnabled does not gate the report (it mirrors CreateNode, which only filters
        // children); the caller decides whether to skip a disabled group, so a disabled group still
        // sums its enabled children.
        var group = new AudioEffectGroup { IsEnabled = false };
        group.Children.Add(CreateLimiterEffect(5f));
        group.Children.Add(CreateLimiterEffect(10f));

        int expected = ExpectedLookaheadSamples(5f, SampleRate) + ExpectedLookaheadSamples(10f, SampleRate);
        Assert.That(group.GetLatencySamples(SampleRate), Is.EqualTo(expected));
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
        // A mixer aligns its branches to the slowest, so the total is the max branch latency, not the
        // sum — guards against double-counting sibling inputs.
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

    [TestCase(0)]
    [TestCase(-1)]
    public void GetLatencySamples_NonPositiveSampleRate_Throws(int sampleRate)
    {
        using var gain = new GainNode { Gain = Property.CreateAnimatable(100f) };
        using var limiterNode = CreateLimiterNode(5f);
        var limiterEffect = CreateLimiterEffect(5f);
        var group = new AudioEffectGroup();

        // Every reporting entry point guards the rate, so the contract is uniform across node types,
        // not just the ones that reach LimiterParameters.ToLatencySamples.
        Assert.Throws<ArgumentOutOfRangeException>(() => gain.GetLatencySamples(sampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => limiterNode.GetLatencySamples(sampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => limiterEffect.GetLatencySamples(sampleRate));
        Assert.Throws<ArgumentOutOfRangeException>(() => group.GetLatencySamples(sampleRate));
    }
}
