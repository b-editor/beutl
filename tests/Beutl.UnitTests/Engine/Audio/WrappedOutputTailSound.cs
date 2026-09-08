using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

public sealed partial class WrappedOutputTailSound : Sound
{
    internal static int LastFlushSampleCount { get; private set; }

    public WrappedOutputTailSound() => ScanProperties<WrappedOutputTailSound>();

    public float LookaheadMs { get; set; } = 5f;

    internal static void ResetFlushState() => LastFlushSampleCount = -1;

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

        var wrapper = context.AddNode(new RecordingWrapperNode());
        context.Connect(clip, wrapper);
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
