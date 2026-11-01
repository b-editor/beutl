using Beutl.Media;

namespace Beutl.Audio.Graph;

// Continuous source cursor and timestamp conversion shared by independent audio processors.
internal sealed class AudioSourceStream(int sampleRate, int channels, Func<MediaTime, MediaTime> mapTime)
{
    // Allow only sample-boundary quantization, not a short seek.
    private const double SeekToleranceSamples = 2;
    private double _nextOutputStart;
    private bool _sourceCursorMatchesOutputTimeline;
    public long Position { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool CanPassThroughDrain => !IsInitialized || _sourceCursorMatchesOutputTimeline;

    public bool Begin(double outputStartSeconds, double sourceStartSeconds, bool forceReanchor = false)
    {
        bool seek = !IsInitialized || forceReanchor
            || Math.Abs(outputStartSeconds - _nextOutputStart) * sampleRate > SeekToleranceSamples;
        if (seek)
        {
            Position = (long)Math.Round(sourceStartSeconds * sampleRate);
            IsInitialized = true;
        }
        return seek;
    }

    public void TrackPassthrough(AudioProcessContext context, int sampleCount)
    {
        double start = context.TimeRange.Start.ToSeconds();
        Position = (long)Math.Round(start * sampleRate) + sampleCount;
        _nextOutputStart = start + (double)sampleCount / sampleRate;
        IsInitialized = true;
        _sourceCursorMatchesOutputTimeline = true;
    }

    public void Complete(AudioProcessContext context, int sampleCount)
    {
        _nextOutputStart = context.TimeRange.Start.ToSeconds() + (double)sampleCount / sampleRate;
        _sourceCursorMatchesOutputTimeline = false;
    }

    public void Invalidate() => IsInitialized = false;

    public int Read(Span<float> buffer, AudioNode input, AudioProcessContext context, bool draining, long? end = null)
    {
        if (buffer.IsEmpty)
            return 0;
        int requestedFrames = buffer.Length / channels;
        if (end is { } terminal)
        {
            requestedFrames = (int)Math.Min(requestedFrames, Math.Max(0, terminal - Position));
            if (requestedFrames == 0)
                return 0;
        }
        var range = new TimeRange(MediaTime.FromSamples(Position, sampleRate),
            AudioProcessContext.GetDurationForSampleCount(requestedFrames, sampleRate));
        var subContext = new AudioProcessContext(range, sampleRate, context.AnimationSampler, context.OriginalTimeRange)
        {
            ProcessEndTime = context.ProcessEndTime is { } processEnd ? mapTime(processEnd) : null
        };
        // This reader consumes the pooled input fully; ownership does not escape it.
        using AudioBuffer result = draining ? input.Flush(subContext) : input.Process(subContext);
        if (result.ChannelCount != channels)
            throw new InvalidOperationException("Upstream channel count does not match the audio stream.");
        int count = Math.Min(requestedFrames, result.SampleCount);
        for (int channel = 0; channel < channels; channel++)
        {
            ReadOnlySpan<float> samples = result.GetChannelData(channel);
            for (int i = 0; i < count; i++)
                buffer[i * channels + channel] = samples[i];
        }
        Position += count;
        return count * channels;
    }

    public static bool UpstreamChangedAndCapture(AudioNode node, ref List<AudioNode>? snapshot)
    {
        var current = new List<AudioNode>();
        CollectUpstream(node, current, new HashSet<AudioNode>());

        bool changed = snapshot is null || snapshot.Count != current.Count;
        if (!changed)
        {
            for (int i = 0; i < current.Count; i++)
            {
                if (!ReferenceEquals(snapshot![i], current[i]))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
            snapshot = current;

        return changed;
    }

    private static void CollectUpstream(AudioNode node, List<AudioNode> acc, HashSet<AudioNode> visited)
    {
        foreach (var input in node.Inputs)
        {
            // Dedupe so a diamond upstream cannot blow up the walk or perturb the snapshot.
            if (!visited.Add(input))
                continue;

            acc.Add(input);
            CollectUpstream(input, acc, visited);
        }
    }
}
