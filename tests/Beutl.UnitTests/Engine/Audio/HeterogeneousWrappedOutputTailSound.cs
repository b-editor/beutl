using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

public sealed partial class HeterogeneousWrappedOutputTailSound : Sound
{
    internal static int LastFlushSampleCount { get; private set; }

    public HeterogeneousWrappedOutputTailSound() => ScanProperties<HeterogeneousWrappedOutputTailSound>();

    internal static void ResetFlushState() => LastFlushSampleCount = -1;

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var shortSource = context.AddNode(new RecordingLatencyNode(240));
        var shortClip = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(shortSource, shortClip);

        var longSource = context.AddNode(new RecordingLatencyNode(960));
        var longClip = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(longSource, longClip);

        var mixer = context.CreateMixerNode();
        context.Connect(shortClip, mixer);
        context.Connect(longClip, mixer);

        var wrapper = context.AddNode(new RecordingWrapperNode());
        context.Connect(mixer, wrapper);
        context.MarkAsOutput(wrapper);
    }

    private sealed class RecordingWrapperNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => Inputs[0].Process(context);

        public override AudioBuffer Flush(AudioProcessContext context)
        {
            LastFlushSampleCount = context.GetSampleCount();
            return base.Flush(context);
        }
    }

    private sealed class RecordingLatencyNode(int latencySamples) : AudioNode
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
