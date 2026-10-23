using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

public sealed partial class SharedClipFanOutTailSound : Sound
{
    public SharedClipFanOutTailSound() => ScanProperties<SharedClipFanOutTailSound>();

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var source = context.AddNode(new FixedLatencyNode(0));
        var clip = context.CreateClipNode(TimeRange.Value.Start, TimeRange.Value.Duration);
        context.Connect(source, clip);

        var direct = context.AddNode(new ForwardingLatencyNode(0));
        context.Connect(clip, direct);
        var delayed = context.AddNode(new ForwardingLatencyNode(30));
        context.Connect(clip, delayed);

        var mixer = context.CreateMixerNode();
        context.Connect(direct, mixer);
        context.Connect(delayed, mixer);
        context.MarkAsOutput(mixer);
    }

    private sealed class ForwardingLatencyNode(int latencySamples) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => Inputs[0].Process(context);

        public override int GetLatencySamples(int sampleRate) => latencySamples;
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}
