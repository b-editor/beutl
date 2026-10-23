using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;

using static Beutl.UnitTests.Engine.Audio.AudioTestContexts;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioNodeTests
{
    [Test]
    public void AudioContextClearConnections_DisconnectsEveryNodeAndKeepsThem()
    {
        using var context = new AudioContext(48000, 2);
        using var source = new ValueNode();
        using var mixer = new MixerNode { Gains = [0.5f] };
        context.Connect(source, mixer);
        context.MarkAsOutput(mixer);

        context.ClearConnections();

        Assert.Multiple(() =>
        {
            Assert.That(context.Nodes, Is.EqualTo(new AudioNode[] { source, mixer }));
            Assert.That(mixer.Inputs, Is.Empty);
            Assert.That(mixer.Gains, Is.Empty);
            Assert.That(context.GetOutputNodes(), Is.Empty);
        });
    }

    [Test]
    public void TransformingNodeFlush_PreservesMonoLayoutAfterInputRemoval()
    {
        const int sampleRate = 48000;
        const int sampleCount = 64;

        using var input = new MonoValueNode();
        using var gain = new GainNode { Gain = Property.CreateExpression(50f) };
        gain.AddInput(input);

        using var processed = gain.Process(Exact(MediaTime.Zero, sampleCount, sampleRate));
        gain.RemoveInput(input);
        using var flushed = gain.Flush(Exact(ExactDuration(sampleCount, sampleRate), sampleCount, sampleRate));

        Assert.That(processed.ChannelCount, Is.EqualTo(1));
        Assert.That(flushed.ChannelCount, Is.EqualTo(1),
            "A transforming node must retain the layout it emitted when its input is no longer connected.");
    }

    private sealed class ValueNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());
    }

    private sealed class MonoValueNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            RecordProcessedChannelCount(1);
            return new AudioBuffer(context.SampleRate, 1, context.GetSampleCount());
        }
    }
}
