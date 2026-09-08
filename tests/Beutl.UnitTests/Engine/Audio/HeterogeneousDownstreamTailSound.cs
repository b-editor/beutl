using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

public sealed partial class HeterogeneousDownstreamTailSound : Sound
{
    public HeterogeneousDownstreamTailSound() => ScanProperties<HeterogeneousDownstreamTailSound>();

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var firstSource = context.AddNode(new RecordingLatencyNode(20));
        var firstClip = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(firstSource, firstClip);

        var secondSource = context.AddNode(new RecordingLatencyNode(0));
        var secondClip = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(secondSource, secondClip);
        var secondDownstream = context.AddNode(new ForwardingLatencyNode(30));
        context.Connect(secondClip, secondDownstream);

        var mixer = context.CreateMixerNode();
        context.Connect(firstClip, mixer);
        context.Connect(secondDownstream, mixer);
        context.MarkAsOutput(mixer);
    }

    private sealed class RecordingLatencyNode(int latencySamples) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override AudioBuffer Flush(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override int GetLatencySamples(int sampleRate) => latencySamples;
    }

    private sealed class ForwardingLatencyNode(int latencySamples) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => Inputs[0].Process(context);

        public override AudioBuffer Flush(AudioProcessContext context)
            => Inputs[0].Flush(context);

        public override int GetLatencySamples(int sampleRate) => latencySamples;
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}
