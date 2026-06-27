using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

// A self-contained Sound (no media file) whose graph is a clip-local sine leaf feeding a lookahead
// limiter, so a Composer that composes it across two windows exercises the limiter tail held at the
// clip boundary. Top-level partial so the resource source generator emits its Resource.
public sealed partial class LimiterTailSound : Sound
{
    public LimiterTailSound() => ScanProperties<LimiterTailSound>();

    public float LookaheadMs { get; set; } = 5f;

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var source = context.AddNode(new ClipLocalSineNode(context.SampleRate));
        var limiter = context.AddNode(new LimiterNode
        {
            Threshold = Property.CreateAnimatable(LimiterParameters.MaxThresholdDb),
            Release = Property.CreateAnimatable(LimiterParameters.DefaultReleaseMs),
            Lookahead = Property.CreateAnimatable(LookaheadMs),
            MakeupGain = Property.CreateAnimatable(0f),
        });
        context.Connect(source, limiter);

        var clip = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(limiter, clip);
        context.MarkAsOutput(clip);
    }

    // A clip-local sine keyed to the absolute sample index, returning silence for reads past the clip
    // (the precondition Flush relies on). Mirrors the RangeSineNode used by the node-level tests.
    private sealed class ClipLocalSineNode(int sampleRate) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            int count = context.GetSampleCount();
            var buffer = new AudioBuffer(sampleRate, 2, count);
            long startIndex = AudioMath.TimeToSampleIndex(context.TimeRange.Start, sampleRate);
            for (int ch = 0; ch < 2; ch++)
            {
                var data = buffer.GetChannelData(ch);
                for (int i = 0; i < count; i++)
                {
                    data[i] = 0.25f * MathF.Sin(2f * MathF.PI * 200f * (startIndex + i) / sampleRate);
                }
            }

            return buffer;
        }
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}
