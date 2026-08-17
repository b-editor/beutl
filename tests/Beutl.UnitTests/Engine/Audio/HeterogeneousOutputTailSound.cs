using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

// Resource source generation requires a top-level partial Sound.
public sealed partial class HeterogeneousOutputTailSound : Sound
{
    internal static int ShortFlushCount;
    internal static int LongFlushCount;

    public HeterogeneousOutputTailSound() => ScanProperties<HeterogeneousOutputTailSound>();

    internal static void ResetFlushCounts()
    {
        ShortFlushCount = 0;
        LongFlushCount = 0;
    }

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        context.MarkAsOutput(context.AddNode(new RecordingLatencyNode(240, isShort: true)));
        context.MarkAsOutput(context.AddNode(new RecordingLatencyNode(960, isShort: false)));
    }

    private sealed class RecordingLatencyNode(int latencySamples, bool isShort) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        public override AudioBuffer Flush(AudioProcessContext context)
        {
            if (isShort)
                ShortFlushCount++;
            else
                LongFlushCount++;

            return new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
        }

        public override int GetLatencySamples(int sampleRate) => latencySamples;
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}
