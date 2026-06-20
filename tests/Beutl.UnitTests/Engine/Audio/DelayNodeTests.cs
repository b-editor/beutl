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

    // An animatable property with NO animation must behave exactly like a static one (the point of
    // routing on Animation != null, not IsAnimatable). The buffer is stateful, so use a fresh node.
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

    // A seek in either direction is a discontinuity: the delay lines must not carry audio from the
    // previous range. Regression for the tracker that only reset on backward seeks before its pinned
    // start, so forward seeks replayed stale delay-line content.
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

        Assert.Multiple(() =>
        {
            Assert.That(afterSeek.GetChannelData(0).ToArray(),
                Is.EqualTo(freshRun.GetChannelData(0).ToArray()).AsCollection);
            Assert.That(afterSeek.GetChannelData(1).ToArray(),
                Is.EqualTo(freshRun.GetChannelData(1).ToArray()).AsCollection);
        });
    }

    // Contiguous playback is NOT a discontinuity: the delay lines must carry across chunks
    // (the previous chunk's wet tail feeds the next one's first samples).
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

    // Emits a ramp with a configurable channel count so a reused node can be driven first with one
    // channel and then with more.
    private sealed class ConfigurableChannelInputNode(int sampleRate, int channelCount) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            int count = context.GetSampleCount();
            var buffer = new AudioBuffer(sampleRate, channelCount, count);
            int startIndex = (int)(context.TimeRange.Start.TotalSeconds * sampleRate);
            for (int ch = 0; ch < channelCount; ch++)
            {
                Span<float> data = buffer.GetChannelData(ch);
                for (int i = 0; i < count; i++)
                {
                    data[i] = (startIndex + i + 1) * 0.01f * (ch + 1);
                }
            }

            return buffer;
        }
    }

    // The node is cached across Compose() calls; initialization keyed only on sample rate would keep
    // a too-short delay-line array after a channel-count increase, leaving the extra channels silent.
    [Test]
    public void Process_ChannelCountIncreaseOnReusedNode_ProcessesAllChannels()
    {
        using var node = new DelayNode
        {
            DelayTime = Property.Create(200f),
            Feedback = Property.Create(50f),
            DryMix = Property.Create(60f),
            WetMix = Property.Create(40f),
        };

        // First render sizes the cached delay-line array to a single channel.
        node.AddInput(new ConfigurableChannelInputNode(SampleRate, 1));
        node.Process(CreateContext()).Dispose();

        // Re-render on the SAME node with a stereo source: the delay lines must grow to 2 channels.
        node.ClearInputs();
        node.AddInput(new ConfigurableChannelInputNode(SampleRate, 2));
        using AudioBuffer stereo = node.Process(CreateContext());

        float[] right = stereo.GetChannelData(1).ToArray();
        Assert.That(Array.Exists(right, v => v != 0f), Is.True,
            "The second channel must be processed after a channel-count increase on a reused node.");
    }
}
