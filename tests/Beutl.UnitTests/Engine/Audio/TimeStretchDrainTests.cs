using Beutl.Animation;
using Beutl.Animation.Easings;
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

    [TestCase(50f, false, false)]
    [TestCase(200f, false, false)]
    [TestCase(50f, true, false)]
    [TestCase(50f, true, true)]
    public void ClipExtension_AfterTerminalResumesAReusedStretcher(float speed, bool animated, bool globalClock)
    {
        var effect = new TimeStretchEffect
        {
            TimeRange = new TimeRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2))
        };
        effect.Speed.CurrentValue = speed;
        if (animated)
        {
            var animation = new KeyFrameAnimation<float> { UseGlobalClock = globalClock };
            TimeSpan offset = globalClock ? effect.TimeRange.Start : TimeSpan.Zero;
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = offset, Value = speed, Easing = new LinearEasing() });
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = offset + TimeSpan.FromSeconds(2), Value = 200f, Easing = new LinearEasing() });
            effect.Speed.Animation = animation;
        }
        // The linear curve reaches its second keyframe at two seconds; integrate its first second.
        double factor = (animated ? speed + (200 - speed) / 4d : speed) / 100d;
        long sourceBoundary = (long)Math.Round(SampleRate * factor);
        using var context = new AudioContext(SampleRate, 2);
        var source = context.AddNode(new StepSource(sourceBoundary));
        AudioNode stretched = effect.CreateNode(context, source);
        ClipNode clip = context.CreateClipNode(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        context.Connect(stretched, clip);
        using (clip.Process(Context(0, SampleRate))) { }
        int reads = source.ReadStarts.Count;

        context.BeginUpdate(context.Nodes.ToArray());
        context.AddNode(source);
        AudioNode reused = effect.CreateNode(context, source);
        ClipNode extended = context.CreateClipNode(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        context.Connect(reused, extended);
        context.EndUpdate();
        using AudioBuffer output = extended.Process(Context(SampleRate, SampleRate / 2));

        Assert.That(reused, Is.SameAs(stretched));
        Assert.That(extended, Is.Not.SameAs(clip));
        Assert.That(source.ReadStarts.Count, Is.GreaterThan(reads));
        Assert.That(source.ReadStarts[reads], Is.EqualTo(sourceBoundary).Within(1));
        Assert.That(output.GetChannelData(0).ToArray(), Is.All.GreaterThan(0.99f),
            "The newly exposed source audio must play without a seek.");
    }

    [Test]
    public void ClipExtension_WhileFinalOutputIsBufferedReanchorsToCurrentTime()
    {
        var effect = new TimeStretchEffect();
        effect.Speed.CurrentValue = 50;
        using var context = new AudioContext(SampleRate, 2);
        var source = context.AddNode(new StepSource(240));
        AudioNode stretched = effect.CreateNode(context, source);
        ClipNode clip = context.CreateClipNode(TimeSpan.Zero, AudioProcessContext.GetDurationForSampleCount(480, SampleRate));
        context.Connect(stretched, clip);
        using (clip.Process(Context(0, 128))) { }
        Assert.That(source.LastReadEnd, Is.EqualTo(240));
        int reads = source.ReadStarts.Count;

        context.BeginUpdate(context.Nodes.ToArray());
        context.AddNode(source);
        AudioNode reused = effect.CreateNode(context, source);
        ClipNode extended = context.CreateClipNode(TimeSpan.Zero, AudioProcessContext.GetDurationForSampleCount(960, SampleRate));
        context.Connect(reused, extended);
        context.EndUpdate();
        using AudioBuffer output = extended.Process(Context(128, 512));

        using var freshContext = new AudioContext(SampleRate, 2);
        var freshSource = freshContext.AddNode(new StepSource(240));
        AudioNode fresh = effect.CreateNode(freshContext, freshSource);
        ClipNode freshClip = freshContext.CreateClipNode(TimeSpan.Zero, extended.Duration);
        freshContext.Connect(fresh, freshClip);
        using AudioBuffer expected = freshClip.Process(Context(128, 512));

        Assert.That(reused, Is.SameAs(stretched));
        Assert.That(source.ReadStarts.Count, Is.GreaterThan(reads));
        Assert.That(source.ReadStarts[reads], Is.EqualTo(64),
            "A finished stretcher may have read ahead to the old boundary; resume at the mapped playback position.");
        Assert.That(output.GetChannelData(0).ToArray(), Is.EqualTo(expected.GetChannelData(0).ToArray()));
    }

    [Test]
    public void ClipRemoval_ResumesAFlushedStretcherWithoutAFiniteBoundary()
    {
        var effect = new TimeStretchEffect();
        effect.Speed.CurrentValue = 50;
        using var context = new AudioContext(SampleRate, 2);
        var source = context.AddNode(new StepSource(24000));
        AudioNode stretched = effect.CreateNode(context, source);
        ClipNode clip = context.CreateClipNode(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        context.Connect(stretched, clip);
        using (clip.Process(Context(0, SampleRate))) { }
        int reads = source.ReadStarts.Count;

        context.BeginUpdate(context.Nodes.ToArray());
        context.AddNode(source);
        AudioNode reused = effect.CreateNode(context, source);
        context.EndUpdate();
        using AudioBuffer output = reused.Process(Context(SampleRate, SampleRate / 2));

        Assert.That(reused, Is.SameAs(stretched));
        Assert.That(source.ReadStarts.Count, Is.GreaterThan(reads));
        Assert.That(source.ReadStarts[reads], Is.EqualTo(24000));
        Assert.That(output.GetChannelData(0).ToArray(), Is.All.GreaterThan(0.99f));
    }

    [Test]
    public void ClipExtension_AfterPartialDrainResumesLiveReads()
    {
        var effect = new TimeStretchEffect();
        effect.Speed.CurrentValue = 50;
        using var context = new AudioContext(SampleRate, 2);
        var source = context.AddNode(new StepSource(24000));
        AudioNode limiter = CreateLimiter(context, source, 10);
        AudioNode stretched = effect.CreateNode(context, limiter);
        ClipNode clip = context.CreateClipNode(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        context.Connect(stretched, clip);
        using (clip.Process(Context(0, SampleRate))) { }
        using AudioBuffer tail = clip.Flush(Context(SampleRate, 128));
        Assert.That(tail.GetChannelData(0).ToArray(), Has.Some.GreaterThan(0.05f));
        int reads = source.ReadStarts.Count;

        context.BeginUpdate(context.Nodes.ToArray());
        context.AddNode(source);
        context.AddNode(limiter);
        context.Connect(source, limiter);
        AudioNode reused = effect.CreateNode(context, limiter);
        ClipNode extended = context.CreateClipNode(TimeSpan.Zero,
            AudioProcessContext.GetDurationForSampleCount(SampleRate + 400, SampleRate));
        context.Connect(reused, extended);
        context.EndUpdate();
        using AudioBuffer output = extended.Process(Context(SampleRate + 128, 256));

        Assert.That(reused, Is.SameAs(stretched));
        Assert.That(source.ReadStarts.Count, Is.GreaterThan(reads));
        Assert.That(source.ReadStarts[reads], Is.EqualTo(24064),
            "The live clip end can be before the old drain end; resume from the mapped playback position.");
        Assert.That(source.LastReadEnd, Is.LessThanOrEqualTo(24200));
        Assert.That(output.GetChannelData(0).ToArray(), Is.All.Zero,
            "The limiter should prime its live lookahead instead of replaying the cached drain tail.");
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
        public List<long> ReadStarts { get; } = [];
        public override AudioBuffer Process(AudioProcessContext context)
        {
            long start = AudioMath.TimeToSampleIndex(context.TimeRange.Start, SampleRate);
            int count = context.GetSampleCount();
            LastReadEnd = start + count;
            ReadStarts.Add(start);
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
