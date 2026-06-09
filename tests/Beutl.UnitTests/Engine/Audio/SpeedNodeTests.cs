using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SpeedNodeTests
{
    private const int SampleRate = 100;

    // Deterministic stereo source: a ramp keyed off the global sample index, so any change in how
    // SpeedNode requests source ranges shows up in the output.
    private sealed class RampInputNode : AudioNode
    {
        private readonly int _sampleRate;

        public RampInputNode(int sampleRate) => _sampleRate = sampleRate;

        public override AudioBuffer Process(AudioProcessContext context)
        {
            int count = context.GetSampleCount();
            var buffer = new AudioBuffer(_sampleRate, 2, count);
            Span<float> left = buffer.GetChannelData(0);
            Span<float> right = buffer.GetChannelData(1);
            int startIndex = (int)(context.TimeRange.Start.TotalSeconds * _sampleRate);
            for (int i = 0; i < count; i++)
            {
                float v = (startIndex + i) * 0.01f;
                left[i] = v;
                right[i] = -v;
            }

            return buffer;
        }
    }

    private static IProperty<float> AnimatedSpeed(float fromPercent, float toPercent, double durationSeconds)
    {
        var property = Property.CreateAnimatable(fromPercent);
        property.SetAttributes("Speed", []);

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

    private static SpeedNode CreateAnimatedSpeedNode()
    {
        var node = new SpeedNode { Speed = AnimatedSpeed(50f, 200f, 1.0) };
        node.AddInput(new RampInputNode(SampleRate));
        return node;
    }

    private static AudioProcessContext CreateContext()
    {
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        return new AudioProcessContext(range, SampleRate, new AnimationSampler(), null);
    }

    [Test]
    public void ProcessAnimatedSpeed_ProducesExpectedLengthAndFiniteSamples()
    {
        using var node = CreateAnimatedSpeedNode();
        var context = CreateContext();

        using AudioBuffer result = node.Process(context);

        Assert.That(result.SampleCount, Is.EqualTo(context.GetSampleCount()));
        Assert.That(result.ChannelCount, Is.EqualTo(2));

        for (int ch = 0; ch < result.ChannelCount; ch++)
        {
            Span<float> data = result.GetChannelData(ch);
            for (int i = 0; i < data.Length; i++)
            {
                Assert.That(float.IsFinite(data[i]), Is.True,
                    $"channel {ch} sample {i} was not finite ({data[i]}).");
            }
        }
    }

    // Guards the ArrayPool usage in ProcessAnimatedSpeed: two fresh nodes fed identical input must
    // produce identical output, so a pooled speed buffer with leftover stale data would diverge.
    // A node is stateful across renders (WdlResampler filter state), hence a fresh node per render.
    [Test]
    public void ProcessAnimatedSpeed_IsDeterministicAcrossFreshInstances()
    {
        float[][] RenderFresh()
        {
            using var node = CreateAnimatedSpeedNode();
            var context = CreateContext();
            using AudioBuffer buffer = node.Process(context);
            return
            [
                buffer.GetChannelData(0).ToArray(),
                buffer.GetChannelData(1).ToArray(),
            ];
        }

        float[][] first = RenderFresh();
        float[][] second = RenderFresh();

        Assert.Multiple(() =>
        {
            Assert.That(second[0], Is.EqualTo(first[0]).AsCollection);
            Assert.That(second[1], Is.EqualTo(first[1]).AsCollection);
        });
    }
}
