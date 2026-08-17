using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

public sealed partial class SpeedInlineTailSound : Sound
{
    public const int SourceLatencySamples = 100;
    public const float SpeedPercent = 50f;

    public SpeedInlineTailSound() => ScanProperties<SpeedInlineTailSound>();

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var source = context.AddNode(new FixedLatencyNode(SourceLatencySamples));
        var clip = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(source, clip);

        var speed = Property.CreateAnimatable(SpeedPercent);
        var speedNode = context.AddNode(new SpeedNode { Speed = speed });
        context.Connect(clip, speedNode);
        context.MarkAsOutput(speedNode);
    }

    private sealed class FixedLatencyNode(int latencySamples) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override AudioBuffer Flush(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override int GetLatencySamples(int sampleRate) => latencySamples;
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}
