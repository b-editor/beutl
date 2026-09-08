using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

public sealed partial class MixedInlineUnknownTailSound : Sound
{
    internal static int UnknownFlushCount;

    public MixedInlineUnknownTailSound() => ScanProperties<MixedInlineUnknownTailSound>();

    internal static void ResetFlushCount() => UnknownFlushCount = 0;

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        context.MarkAsOutput(context.AddNode(new UnknownTailNode()));

        var source = context.AddNode(new ClipLocalSilenceNode(context.SampleRate));
        var clip = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(source, clip);

        var downstream = context.AddNode(new RecordingLatencyNode(240));
        context.Connect(clip, downstream);
        context.MarkAsOutput(downstream);
    }

    private sealed class UnknownTailNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override AudioBuffer Flush(AudioProcessContext context)
        {
            UnknownFlushCount++;
            var buffer = new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
            buffer.GetChannelData(0).Fill(0.25f);
            buffer.GetChannelData(1).Fill(0.25f);
            return buffer;
        }

        public override int GetLatencySamples(int sampleRate) => int.MaxValue;
    }

    private sealed class RecordingLatencyNode(int latencySamples) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override AudioBuffer Flush(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override int GetLatencySamples(int sampleRate) => latencySamples;
    }

    private sealed class ClipLocalSilenceNode(int sampleRate) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(sampleRate, 2, context.GetSampleCount());
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}
