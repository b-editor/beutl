using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

// Resource source generation requires a top-level partial Sound.
public sealed partial class LimiterDelayTailSound : Sound
{
    public LimiterDelayTailSound() => ScanProperties<LimiterDelayTailSound>();

    public float LookaheadMs { get; set; } = 5f;

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var source = context.AddNode(new ClipLocalSineNode(context.SampleRate));
        var limiter = new LimiterEffect
        {
            Threshold = { CurrentValue = LimiterParameters.MaxThresholdDb },
            Release = { CurrentValue = LimiterParameters.DefaultReleaseMs },
            Lookahead = { CurrentValue = LookaheadMs },
            MakeupGain = { CurrentValue = 0f },
        };

        var delay = new DelayEffect
        {
            DelayTime = { CurrentValue = 1f },
            Feedback = { CurrentValue = 100f },
            DryMix = { CurrentValue = 0f },
            WetMix = { CurrentValue = 100f },
        };

        var group = new AudioEffectGroup();
        group.Children.Add(limiter);
        group.Children.Add(delay);
        var output = group.CreateNode(context, source);

        var clip = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(output, clip);
        context.MarkAsOutput(clip);
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
