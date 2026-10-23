using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine;

// Media positions on the frame and sample grids are exact in flicks, NTSC frames and common sample rates alike (#2885).
[TestFixture]
public sealed class MediaTimeMediaPositionTests
{
    private static readonly Rational[] s_videoRates = [new(24000, 1001), new(30000, 1001), new(30)];
    private static readonly int[] s_sampleRates = [44_100, 48_000, 96_000];

    [OneTimeSetUp]
    public void RegisterDecoder() => TestMediaHelper.RegisterTestDecoder();

    // A time read back from a file falls up to a tick short of its frame and still shows that frame.
    [TestCaseSource(nameof(s_videoRates))]
    public void SourceVideo_draws_the_frame_of_the_grid(Rational rate)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(16, 16, rate, 300)));
        var video = new SourceVideo
        {
            Source = { Value = source },
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromFrames(300, rate)) },
        };
        using var resource = (SourceVideo.Resource)video.ToResource(CompositionContext.Default);

        var mismatches = new List<string>();
        for (int frame = 0; frame < 300; frame++)
        {
            MediaTime time = MediaTime.FromFrames(frame, rate);
            foreach (MediaTime at in (MediaTime[])[time, Saved(time)])
            {
                bool versionBumped = false;
                resource.Reconcile(video, new CompositionContext(at), ref versionBumped);
                var node = new ContainerRenderNode();
                video.DrawInternal(new GraphicsContext2D(node, new Size(16, 16)), resource);
                if (node.Children.Single() is not VideoSourceRenderNode { Frame: var drawn } || drawn != frame)
                    mismatches.Add($"frame {frame} at {at.Flicks} flicks");
            }
        }

        Assert.That(mismatches, Is.Empty);
    }

    [TestCaseSource(nameof(s_sampleRates))]
    public void Sample_positions_and_counts_are_exact(int sampleRate)
    {
        var mismatches = new List<string>();
        MediaTime length = MediaTime.FromSamples(4096, sampleRate);
        foreach (long sample in (long[])[0, 1, 1023, 1024, sampleRate - 1, sampleRate, 7L * sampleRate + 13, 3600L * sampleRate])
        {
            MediaTime start = MediaTime.FromSamples(sample, sampleRate);
            var context = new AudioProcessContext(new TimeRange(start, length), sampleRate, new AnimationSampler(), null);
            if (context.GetSampleCount() != 4096
                || context.GetTimeForSample(4095) != start + MediaTime.FromSamples(4095, sampleRate)
                || context.GetSampleForTime(start + MediaTime.FromSamples(17, sampleRate)) != 17
                || AudioProcessContext.GetDurationForSampleCount(4096, sampleRate) != length
                || !context.ContinuesFrom(start))
            {
                mismatches.Add($"sample {sample}");
            }
        }

        Assert.That(mismatches, Is.Empty);
    }

    // At rates whose samples are not whole flicks, counts stay exact and a resampled source is read without gaps or repeats.
    [TestCase(47_952)]
    [TestCase(384_000)]
    public void Rates_that_are_not_whole_flicks_keep_counts_and_contiguous_reads(int sourceRate)
    {
        var mismatches = new List<string>();
        for (int count = 1; count <= 256; count++)
        {
            foreach (MediaTime duration in (MediaTime[])[
                AudioProcessContext.GetDurationForSampleCount(count, sourceRate),
                SourceSound.GetWaveformChunkDuration(count, sourceRate)])
            {
                if (AudioProcessContext.GetSampleCount(new TimeRange(MediaTime.Zero, duration), sourceRate) != count)
                    mismatches.Add($"count {count}");
            }
        }

        using var source = new ReadRecordingNode();
        using var resample = new ResampleNode { SourceSampleRate = sourceRate };
        resample.AddInput(source);
        for (int block = 0; block < 32; block++)
        {
            var range = new TimeRange(MediaTime.FromSamples(block * 480L, 48_000), MediaTime.FromSamples(480, 48_000));
            resample.Process(new AudioProcessContext(range, 48_000, new AnimationSampler(), null)).Dispose();
        }

        for (int i = 1; i < source.Reads.Count; i++)
        {
            (long start, int count) = source.Reads[i - 1];
            if (source.Reads[i].Start != start + count)
                mismatches.Add($"read {i} starts at {source.Reads[i].Start}, not {start + count}");
        }

        Assert.That(mismatches, Is.Empty);
    }

    [TestCaseSource(nameof(s_sampleRates))]
    public void SoundSource_reads_the_samples_of_a_time_range(int sampleRate)
    {
        var source = new SoundSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(sampleRate, 2, 2)));
        using var resource = (SoundSource.Resource)source.ToResource(CompositionContext.Default);

        Assert.That(resource.Read(MediaTime.FromSamples(sampleRate / 2, sampleRate), MediaTime.FromSamples(1000, sampleRate), out AudioBuffer? sound), Is.True);
        using (sound)
            Assert.That(sound!.SampleCount, Is.EqualTo(1000));
    }

    private static MediaTime Saved(MediaTime time)
        => MediaTime.FromTicks(time.ToTicks(MidpointRounding.ToNegativeInfinity), MidpointRounding.ToPositiveInfinity);

    // Records the sample each block starts at, truncated as a source reading a file does.
    private sealed class ReadRecordingNode : AudioNode
    {
        public List<(long Start, int Count)> Reads { get; } = [];

        public override AudioBuffer Process(AudioProcessContext context)
        {
            int count = context.GetSampleCount();
            Reads.Add((context.TimeRange.Start.ToSamples(context.SampleRate, MidpointRounding.ToZero), count));
            return new AudioBuffer(context.SampleRate, 2, count);
        }
    }
}
