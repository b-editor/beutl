using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class DelayNodeTests
{
    private const int SampleRate = 100;

    // Deterministic stereo source: a ramp keyed off the requested global sample index.
    private sealed class RampInputNode(int sampleRate) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            int count = context.GetSampleCount();
            var buffer = new AudioBuffer(sampleRate, 2, count);
            Span<float> left = buffer.GetChannelData(0);
            Span<float> right = buffer.GetChannelData(1);
            int startIndex = (int)(context.TimeRange.Start.TotalSeconds * sampleRate);
            for (int i = 0; i < count; i++)
            {
                float v = (startIndex + i) * 0.01f;
                left[i] = v;
                right[i] = -v;
            }

            return buffer;
        }
    }

    private static AudioProcessContext CreateContext() =>
        new(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), SampleRate, new AnimationSampler(), null);

    private static float[][] Render(Func<float, IProperty<float>> makeProperty)
    {
        using var node = new DelayNode
        {
            DelayTime = makeProperty(200f),
            Feedback = makeProperty(50f),
            DryMix = makeProperty(60f),
            WetMix = makeProperty(40f),
        };
        node.AddInput(new RampInputNode(SampleRate));

        using AudioBuffer buffer = node.Process(CreateContext());
        return
        [
            buffer.GetChannelData(0).ToArray(),
            buffer.GetChannelData(1).ToArray(),
        ];
    }

    private static IProperty<float> Animatable(float value)
    {
        var property = Property.CreateAnimatable(value);
        property.SetAttributes("P", []);
        return property;
    }

    // An animatable property with NO animation must behave exactly like a static one: both feed
    // CurrentValue every sample. This is the point of routing on Animation != null, not IsAnimatable.
    // The CircularBuffer is stateful across renders, so each render uses a fresh node.
    [Test]
    public void Process_AnimatableParamsWithoutAnimation_MatchesNonAnimatableStatic()
    {
        float[][] animatable = Render(Animatable);
        float[][] simple = Render(v => Property.Create(v));

        Assert.Multiple(() =>
        {
            Assert.That(animatable[0], Is.EqualTo(simple[0]).AsCollection);
            Assert.That(animatable[1], Is.EqualTo(simple[1]).AsCollection);
        });
    }
}
