using System;
using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Graph;

public sealed class AudioProcessContext
{
    public AudioProcessContext(
        TimeRange timeRange,
        int sampleRate,
        AnimationSampler animationSampler,
        TimeRange? originalTimeRange
    )
    {
        ArgumentNullException.ThrowIfNull(animationSampler);

        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                "Sample rate must be positive."
            );

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
        return GetSampleCount(TimeRange, SampleRate);
    }

    /// <summary>
    /// Returns the number of audio samples that cover <paramref name="range"/> at the given <paramref name="sampleRate"/>.
    /// </summary>
    /// <remarks>
    /// Always rounds up via <see cref="Math.Ceiling(double)"/> so non-integer-second durations never under-allocate.
    /// Both per-node audio paths and the silence fallback in <see cref="Composing.Composer"/> must route through
    /// this helper to stay in sync; replacing it with truncation will desynchronise mix and silent buffers by one sample.
    /// </remarks>
    public static int GetSampleCount(TimeRange range, int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                "Sample rate must be positive."
            );
        if (range.Duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(range),
                $"Duration must be non-negative; was {range.Duration}."
            );

        double samples = Math.Ceiling(range.Duration.TotalSeconds * sampleRate);
        if (samples > int.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(range),
                $"Sample count {samples} exceeds Int32.MaxValue at sampleRate={sampleRate}."
            );

        return (int)samples;
    }

    public TimeSpan GetTimeForSample(int sampleIndex)
    {
        if (sampleIndex < 0)
            throw new ArgumentOutOfRangeException(
                nameof(sampleIndex),
                "Sample index must be non-negative."
            );

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
