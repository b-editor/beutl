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

    private static AudioProcessContext ContextAt(double startSeconds) =>
        new(new TimeRange(TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(1)), SampleRate,
            new AnimationSampler(), null);

    private static DelayNode CreateStaticNode()
    {
        var node = new DelayNode
        {
            DelayTime = Property.Create(200f),
            Feedback = Property.Create(50f),
            DryMix = Property.Create(60f),
            WetMix = Property.Create(40f),
        };
        node.AddInput(new RampInputNode(SampleRate));
        return node;
    }

    // A seek in either direction is a discontinuity: the delay lines must not carry audio from
    // the previously rendered range. Regression for the tracker that was pinned to the first
    // rendered start and only reset when seeking to before it, so forward seeks (and backward
    // seeks to anywhere after the pinned start) replayed stale delay-line content.
    [Test]
    public void Process_ForwardSeek_ResetsDelayLines()
    {
        using var seeked = CreateStaticNode();
        seeked.Process(ContextAt(0)).Dispose();
        using AudioBuffer afterSeek = seeked.Process(ContextAt(2));

        using var fresh = CreateStaticNode();
        using AudioBuffer freshRun = fresh.Process(ContextAt(2));

        Assert.Multiple(() =>
        {
            Assert.That(afterSeek.GetChannelData(0).ToArray(),
                Is.EqualTo(freshRun.GetChannelData(0).ToArray()).AsCollection);
            Assert.That(afterSeek.GetChannelData(1).ToArray(),
                Is.EqualTo(freshRun.GetChannelData(1).ToArray()).AsCollection);
        });
    }

    [Test]
    public void Process_BackwardSeekAfterPlayback_ResetsDelayLines()
    {
        using var seeked = CreateStaticNode();
        seeked.Process(ContextAt(0)).Dispose();
        seeked.Process(ContextAt(1)).Dispose();
        using AudioBuffer afterSeek = seeked.Process(ContextAt(1));

        using var fresh = CreateStaticNode();
        using AudioBuffer freshRun = fresh.Process(ContextAt(1));

        Assert.That(afterSeek.GetChannelData(0).ToArray(),
            Is.EqualTo(freshRun.GetChannelData(0).ToArray()).AsCollection);
    }

    // Contiguous playback is NOT a discontinuity: the delay lines must carry across chunks
    // (the wet tail of the previous chunk feeds the first samples of the next one).
    [Test]
    public void Process_ContiguousChunks_PreserveDelayLines()
    {
        using var continuous = CreateStaticNode();
        continuous.Process(ContextAt(0)).Dispose();
        using AudioBuffer second = continuous.Process(ContextAt(1));

        using var fresh = CreateStaticNode();
        using AudioBuffer freshRun = fresh.Process(ContextAt(1));

        Assert.That(second.GetChannelData(0).ToArray(),
            Is.Not.EqualTo(freshRun.GetChannelData(0).ToArray()).AsCollection);
    }
}
