using System;
using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Graph;

public sealed class AudioProcessContext
{
    public AudioProcessContext(TimeRange timeRange, int sampleRate, AnimationSampler animationSampler, TimeRange? originalTimeRange)
    {
        ArgumentNullException.ThrowIfNull(animationSampler);

        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");

        OriginalTimeRange = originalTimeRange ?? timeRange;
        TimeRange = timeRange;
        SampleRate = sampleRate;
        AnimationSampler = animationSampler;
    }

    public TimeRange TimeRange { get; }

    public TimeRange OriginalTimeRange { get; }

    public int SampleRate { get; }

    public AnimationSampler AnimationSampler { get; }

    public int GetSampleCount()
    {
        return (int)(TimeRange.Duration.TotalSeconds * SampleRate);
    }

    public TimeSpan GetTimeForSample(int sampleIndex)
    {
        if (sampleIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(sampleIndex), "Sample index must be non-negative.");

        var offsetSeconds = (double)sampleIndex / SampleRate;
        return TimeRange.Start + TimeSpan.FromSeconds(offsetSeconds);
    }

    public int GetSampleForTime(TimeSpan time)
    {
        var offset = time - TimeRange.Start;
        if (offset < TimeSpan.Zero)
            return -1;

        return (int)(offset.TotalSeconds * SampleRate);
    }
}
