using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

// Resource source generation requires a top-level partial Sound.
public sealed partial class LimiterDelayTailSound : Sound
{
    public LimiterDelayTailSound() => ScanProperties<LimiterDelayTailSound>();

    public float LookaheadMs { get; set; } = 5f;

    // When set, the lookahead falls from LookaheadMs to 0 one sample before this time.
    public TimeSpan? LookaheadRampEnd { get; set; }

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var source = context.AddNode(new AbsoluteSineNode(context.SampleRate));
        var limiter = new LimiterEffect
        {
            Threshold = { Value = LimiterParameters.MaxThresholdDb },
            Release = { Value = LimiterParameters.DefaultReleaseMs },
            Lookahead = { Value = LookaheadMs },
            MakeupGain = { Value = 0f },
        };
        if (LookaheadRampEnd is { } rampEnd)
        {
            var oneSample = AudioProcessContext.GetDurationForSampleCount(1, context.SampleRate);
            limiter.Lookahead.SetKeyFrameAnimation(new KeyFrameAnimation<float>
            {
                KeyFrames =
                {
                    new KeyFrame<float>
                    {
                        KeyTime = TimeSpan.Zero,
                        Value = LookaheadMs,
                        Easing = new LinearEasing(),
                    },
                    new KeyFrame<float>
                    {
                        KeyTime = rampEnd - oneSample,
                        Value = 0f,
                        Easing = new LinearEasing(),
                    },
                    new KeyFrame<float>
                    {
                        KeyTime = TimeRange.Duration,
                        Value = 0f,
                        Easing = new LinearEasing(),
                    },
                },
            });
        }

        var delay = new DelayEffect
        {
            DelayTime = { Value = 1f },
            Feedback = { Value = 100f },
            DryMix = { Value = 0f },
            WetMix = { Value = 100f },
        };

        var group = new AudioEffectGroup();
        group.Children.Add(limiter);
        group.Children.Add(delay);
        var output = group.CreateNode(context, source);

        var clip = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(output, clip);
        context.MarkAsOutput(clip);
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}
