using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

// Resource source generation requires a top-level partial Sound.
public sealed partial class UnboundedSpeedTailSound : Sound
{
    internal static int FlushCount;

    public UnboundedSpeedTailSound() => ScanProperties<UnboundedSpeedTailSound>();

    internal static void ResetFlushCount() => FlushCount = 0;

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var source = context.AddNode(new ClipLocalSineNode(context.SampleRate));
        var limiter = context.AddNode(new LimiterNode
        {
            Threshold = Property.CreateAnimatable(LimiterParameters.MaxThresholdDb),
            Release = Property.CreateAnimatable(LimiterParameters.DefaultReleaseMs),
            Lookahead = Property.CreateAnimatable(5f),
            MakeupGain = Property.CreateAnimatable(0f),
        });
        context.Connect(source, limiter);

        var speed = Property.CreateAnimatable(100f);
        speed.Animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float>
                {
                    KeyTime = TimeSpan.Zero,
                    Value = 100f,
                    Easing = new UnknownRangeEasing(),
                },
                new KeyFrame<float>
                {
                    KeyTime = TimeSpan.FromSeconds(1),
                    Value = 100f,
                    Easing = new UnknownRangeEasing(),
                },
            },
        };
        var speedNode = context.AddNode(new SpeedNode { Speed = speed });
        context.Connect(limiter, speedNode);

        var counting = context.AddNode(new CountingFlushNode());
        context.Connect(speedNode, counting);

        var clip = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(counting, clip);
        context.MarkAsOutput(clip);
    }

    private sealed class CountingFlushNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => Inputs[0].Process(context);

        public override AudioBuffer Flush(AudioProcessContext context)
        {
            FlushCount++;
            return Inputs[0].Flush(context);
        }
    }

    private sealed class UnknownRangeEasing : Easing
    {
        public override float Ease(float progress) => progress;
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
