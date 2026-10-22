using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Engine;

using static Beutl.UnitTests.Engine.Audio.AudioTestProperties;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class GainNodeTests
{
    private const int SampleRate = 100;

    private static AudioProcessContext CreateContext() =>
        AudioTestContexts.CreateContext(TimeSpan.Zero, TimeSpan.FromSeconds(1), SampleRate);

    // Animatable Gain with no animation assigned must use the static CurrentValue, not the
    // per-sample path.
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
        using var node = new GainNode { Gain = Ramp("Gain", 0f, 100f, 1.0) };
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

    // Each sample gets the gain the property gives video at the same time (#1818).
    [Test]
    public void Process_GainWithAnimation_MatchesThePropertyValue()
    {
        IExpressionProperty<float> gain = Ramp("Gain", 0f, 100f, 1.0);
        using var node = new GainNode { Gain = gain };
        node.AddInput(new ConstantInputNode(SampleRate, 1, SampleRate, 1.0f));

        using AudioBuffer result = node.Process(CreateContext());

        Span<float> data = result.GetChannelData(0);
        for (int i = 0; i < data.Length; i++)
        {
            float expected = gain.GetValue(new CompositionContext(TimeSpan.FromSeconds(i / (double)SampleRate))) / 100f;
            Assert.That(data[i], Is.EqualTo(expected).Within(1e-6f), $"sample {i}");
        }
    }

    // CreateGainNode reuses the previous frame's GainNode by matching its Gain reference.
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
