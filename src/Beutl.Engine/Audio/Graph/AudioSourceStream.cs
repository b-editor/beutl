using Beutl.Media;

namespace Beutl.Audio.Graph;

// Continuous source cursor and timestamp conversion shared by independent audio processors.
internal sealed class AudioSourceStream(int sampleRate, int channels, Func<TimeSpan, TimeSpan> mapTime)
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
        double start = context.TimeRange.Start.TotalSeconds;
        Position = (long)Math.Round(start * sampleRate) + sampleCount;
        _nextOutputStart = start + (double)sampleCount / sampleRate;
        IsInitialized = true;
        _sourceCursorMatchesOutputTimeline = true;
    }

    public void Complete(AudioProcessContext context, int sampleCount)
    {
        _nextOutputStart = context.TimeRange.Start.TotalSeconds + (double)sampleCount / sampleRate;
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
        // Quantize from the integer cursor and bias only timestamps that would repeat a sample upstream.
        long ticks = checked((long)Math.Ceiling(Position * (double)TimeSpan.TicksPerSecond / sampleRate));
        if (AudioMath.TimeToSampleIndex(TimeSpan.FromTicks(ticks), sampleRate) < Position)
            ticks = checked(ticks + 1);
        var range = new TimeRange(TimeSpan.FromTicks(ticks),
            AudioProcessContext.GetDurationForSampleCount(requestedFrames, sampleRate));
        var subContext = new AudioProcessContext(range, sampleRate, context.AnimationSampler, context.OriginalTimeRange)
        {
            ProcessEndTime = context.ProcessEndTime is { } processEnd ? mapTime(processEnd) : null
        };
        // This reader consumes the pooled input fully; ownership does not escape it.
        using AudioBuffer result = draining ? input.Flush(subContext) : input.Process(subContext);
        ReadOnlySpan<float> left = result.GetChannelData(0);
        ReadOnlySpan<float> right = result.ChannelCount > 1 ? result.GetChannelData(1) : left;
        int count = Math.Min(requestedFrames, result.SampleCount);
        for (int i = 0; i < count; i++)
        {
            buffer[i * channels] = left[i];
            buffer[i * channels + 1] = right[i];
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
