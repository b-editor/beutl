using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class TimeStretchDrainTests
{
    private const int SampleRate = 48000;

    [Test]
    public void TrimmedClip_DrainDoesNotContainPostTrimAudio()
    {
        using var context = new AudioContext(SampleRate, 2);
        var source = context.AddNode(new StepSource(24000));
        AudioNode limiter = CreateLimiter(context, source);
        var effect = new TimeStretchEffect();
        effect.Speed.CurrentValue = 50;
        AudioNode stretched = effect.CreateNode(context, limiter);
        var clip = context.AddNode(new ClipNode { Duration = TimeSpan.FromSeconds(1) });
        clip.AddInput(stretched);

        using (clip.Process(Context(0, SampleRate))) { }
        int latency = stretched.GetDrainLatencySamples(SampleRate);
        using AudioBuffer tail = clip.Flush(Context(SampleRate, latency));

        Assert.That(source.LastReadEnd, Is.LessThanOrEqualTo(24000),
            "Analysis lookahead must not advance the live upstream beyond the trim boundary.");
        Assert.That(tail.GetChannelData(0).ToArray(), Is.All.LessThanOrEqualTo(0.10001f),
            "Post-trim audio must not replace the limiter's retained pre-trim samples.");
        Assert.That(tail.GetChannelData(0).ToArray(), Has.Some.GreaterThan(0.05f));
    }

    [Test]
    public void GroupTimeStretch_ReadsTheLastChildsHeldTail()
    {
        using var context = new AudioContext(SampleRate, 2);
        var source = context.AddNode(new EndMarkerSource());
        AudioNode limiter = CreateLimiter(context, source, 10);
        var clip = context.AddNode(new ClipNode { Duration = TimeSpan.FromSeconds(1) });
        clip.AddInput(limiter);
        var mixer = context.AddNode(new MixerNode());
        mixer.AddInput(clip);
        mixer.SetBranchEndTime(clip, TimeSpan.FromSeconds(1));
        var effect = new TimeStretchEffect();
        effect.Speed.CurrentValue = 50;
        AudioNode stretched = effect.CreateNode(context, mixer);

        using AudioBuffer output = stretched.Process(Context(0, SampleRate * 2 + 1920));

        Assert.That(stretched.GetFiniteSourceEndSample(SampleRate), Is.EqualTo(96960));
        Assert.That(output.GetChannelData(0).Slice(SampleRate * 2, 960).ToArray(), Has.Some.GreaterThan(0.05f),
            "The child limiter's samples should remain audible after the child's nominal end.");
    }

    private static AudioNode CreateLimiter(AudioContext context, AudioNode source, float lookahead = 1)
    {
        var effect = new LimiterEffect();
        effect.Lookahead.CurrentValue = lookahead;
        effect.Threshold.CurrentValue = 0;
        return effect.CreateNode(context, source);
    }

    private static AudioProcessContext Context(int start, int count)
        => new(new TimeRange(TimeSpan.FromSeconds(start / (double)SampleRate),
                AudioProcessContext.GetDurationForSampleCount(count, SampleRate)),
            SampleRate, new AnimationSampler(), null);

    private sealed class StepSource(long boundary) : AudioNode
    {
        public long LastReadEnd { get; private set; }
        public override AudioBuffer Process(AudioProcessContext context)
        {
            long start = AudioMath.TimeToSampleIndex(context.TimeRange.Start, SampleRate);
            int count = context.GetSampleCount();
            LastReadEnd = start + count;
            var output = new AudioBuffer(SampleRate, 2, count);
            for (int channel = 0; channel < 2; channel++)
            {
                Span<float> data = output.GetChannelData(channel);
                for (int i = 0; i < count; i++)
                    data[i] = start + i < boundary ? 0.1f : 1f;
            }
            return RecordProcessedOutput(output);
        }
    }

    private sealed class EndMarkerSource : AudioNode
    {
        internal override double? GetFiniteSourceEndSample(int sampleRate) => sampleRate;
        public override AudioBuffer Process(AudioProcessContext context)
        {
            long start = AudioMath.TimeToSampleIndex(context.TimeRange.Start, SampleRate);
            var output = new AudioBuffer(SampleRate, 2, context.GetSampleCount());
            for (int channel = 0; channel < 2; channel++)
            {
                Span<float> data = output.GetChannelData(channel);
                for (int i = 0; i < data.Length; i++)
                    data[i] = start + i >= SampleRate - 480 && start + i < SampleRate ? 0.25f : 0;
            }
            return RecordProcessedOutput(output);
        }
    }
}
