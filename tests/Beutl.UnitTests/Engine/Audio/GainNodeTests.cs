using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class GainNodeTests
{
    private const int SampleRate = 100;

    private static IProperty<float> AnimatedGain(float fromPercent, float toPercent, double durationSeconds)
    {
        var property = Property.CreateAnimatable(fromPercent);
        property.SetAttributes("Gain", []);

        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = fromPercent,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(durationSeconds),
            Value = toPercent,
            Easing = new LinearEasing()
        });
        property.Animation = animation;
        return property;
    }

    private static AudioProcessContext CreateContext() =>
        new(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), SampleRate, new AnimationSampler(), null);

    // Animatable Gain with no animation assigned must use the static CurrentValue, not the
    // per-sample path. Characterizes the behavior-preserving IsAnimatable -> Animation != null change.
    [Test]
    public void Process_AnimatableGainWithoutAnimation_AppliesCurrentValue()
    {
        var gain = Property.CreateAnimatable(50f); // 50% -> factor 0.5
        gain.SetAttributes("Gain", []);
        using var node = new GainNode { Gain = gain };
        node.AddInput(new ConstantInputNode(SampleRate, 2, SampleRate, 1.0f));

        using AudioBuffer result = node.Process(CreateContext());

        Assert.That(result.SampleCount, Is.EqualTo(SampleRate));
        Assert.That(result.ChannelCount, Is.EqualTo(2));
        for (int ch = 0; ch < result.ChannelCount; ch++)
        {
            Span<float> data = result.GetChannelData(ch);
            for (int i = 0; i < data.Length; i++)
            {
                Assert.That(data[i], Is.EqualTo(0.5f).Within(1e-6f),
                    $"channel {ch} sample {i}");
            }
        }
    }

    // A real keyframe animation must drive the per-sample path: output ramps with the gain, so the
    // last sample is louder than the first.
    [Test]
    public void Process_GainWithAnimation_AppliesAnimatedValues()
    {
        using var node = new GainNode { Gain = AnimatedGain(0f, 100f, 1.0) };
        node.AddInput(new ConstantInputNode(SampleRate, 1, SampleRate, 1.0f));

        using AudioBuffer result = node.Process(CreateContext());

        Span<float> data = result.GetChannelData(0);
        Assert.That(data[0], Is.EqualTo(0f).Within(1e-3f));
        Assert.That(data[^1], Is.GreaterThan(data[0]));
        foreach (float sample in data)
        {
            Assert.That(float.IsFinite(sample), Is.True);
        }
    }

    // CreateGainNode reuses a matching GainNode from the previous frame (so streaming/resampler state
    // survives a graph rebuild). The reuse path matches by Gain reference, so the node it returns already
    // carries that exact Gain — this guards the removal of the redundant `existing.Gain = gain` write that
    // the `required init` Gain makes impossible.
    [Test]
    public void CreateGainNode_ReusesPreviousNodeMatchingByGain()
    {
        var gain = Property.CreateAnimatable(50f);
        var previous = new GainNode { Gain = gain };
        using var context = new AudioContext(SampleRate, 2, new[] { previous });

        GainNode created = context.CreateGainNode(gain);

        Assert.That(created, Is.SameAs(previous), "must reuse the previous-frame node matching by Gain reference");
        Assert.That(created.Gain, Is.SameAs(gain), "the reused node must keep the supplied Gain");
    }
}
