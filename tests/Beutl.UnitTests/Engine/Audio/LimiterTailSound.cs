using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

// Resource source generation requires a top-level partial Sound.
public sealed partial class LimiterTailSound : Sound
{
    internal static int LastFlushSampleCount { get; private set; }

    public LimiterTailSound() => ScanProperties<LimiterTailSound>();

    public float LookaheadMs { get; set; } = 5f;

    public bool LimiterBeforeClip { get; set; } = true;

    public bool LimiterAfterClip { get; set; }

    // Wraps the output in a node that records the sample count of its last flush.
    public bool WrapOutput { get; set; }

    internal static void ResetFlushState() => LastFlushSampleCount = -1;

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        AudioNode output = context.AddNode(new AbsoluteSineNode(context.SampleRate));
        if (LimiterBeforeClip)
        {
            output = ConnectLimiter(context, output);
        }

        var clip = context.CreateClipNode(TimeRange.Value.Start, TimeRange.Value.Duration);
        context.Connect(output, clip);
        output = clip;

        if (LimiterAfterClip)
        {
            output = ConnectLimiter(context, output);
        }

        if (WrapOutput)
        {
            var wrapper = context.AddNode(new RecordingWrapperNode());
            context.Connect(output, wrapper);
            output = wrapper;
        }

        context.MarkAsOutput(output);
    }

    private AudioNode ConnectLimiter(AudioContext context, AudioNode input)
    {
        var limiter = context.AddNode(new LimiterNode
        {
            Threshold = Property.CreateAnimatable(LimiterParameters.MaxThresholdDb),
            Release = Property.CreateAnimatable(LimiterParameters.DefaultReleaseMs),
            Lookahead = Property.CreateAnimatable(LookaheadMs),
            MakeupGain = Property.CreateAnimatable(0f),
        });
        context.Connect(input, limiter);
        return limiter;
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

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }
}
