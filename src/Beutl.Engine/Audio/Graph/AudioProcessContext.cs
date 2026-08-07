using System;
using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Graph;

public sealed class AudioProcessContext
{
    private const long TimestampQuantizationToleranceTicks = 1;

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
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        if (range.Duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(range), $"Duration must be non-negative; was {range.Duration}.");

        double samples = Math.Ceiling(range.Duration.TotalSeconds * sampleRate);
        if (samples > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(range), $"Sample count {samples} exceeds Int32.MaxValue at sampleRate={sampleRate}.");

        return (int)samples;
    }

    /// <summary>
    /// Returns a representable duration whose ceiling-based sample count is exactly
    /// <paramref name="sampleCount"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TimeSpan.FromSeconds(double)"/> may round a quotient upward by one tick. Feeding that
    /// duration back through <see cref="GetSampleCount(TimeRange, int)"/> can therefore consume one
    /// extra sample. Taking the greatest whole-tick duration that does not exceed the exact sample
    /// boundary keeps stateful audio nodes and their callers on the same sample count.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleCount"/> is negative, <paramref name="sampleRate"/> is not positive, or
    /// the requested sample count cannot be represented at the given rate with <see cref="TimeSpan"/>
    /// tick precision.
    /// </exception>
    public static TimeSpan GetDurationForSampleCount(int sampleCount, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        if (sampleCount == 0)
            return TimeSpan.Zero;

        long ticks = (long)sampleCount * TimeSpan.TicksPerSecond / sampleRate;
        if (ticks == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                $"Sample rate {sampleRate} is too high to represent {sampleCount} samples as a positive TimeSpan.");
        }

        var duration = TimeSpan.FromTicks(ticks);
        int roundTripped = GetSampleCount(new TimeRange(TimeSpan.Zero, duration), sampleRate);
        while (roundTripped > sampleCount && ticks > 0)
        {
            duration = TimeSpan.FromTicks(--ticks);
            roundTripped = GetSampleCount(new TimeRange(TimeSpan.Zero, duration), sampleRate);
        }

        if (roundTripped != sampleCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                $"Could not represent exactly {sampleCount} samples at sampleRate={sampleRate}; "
                + $"the closest duration produced {roundTripped} samples.");
        }

        return duration;
    }

    /// <summary>
    /// Returns whether this chunk continues directly from a previous chunk that ended at <paramref name="previousEnd"/>.
    /// </summary>
    /// <remarks>
    /// Independently rounded sample boundaries may differ by one tick. A one-tick gap or overlap is
    /// contiguous; two ticks or more is a seek/edit boundary. Stateful nodes must use this helper so
    /// a chain resets consistently.
    /// </remarks>
    public bool ContinuesFrom(TimeSpan previousEnd)
    {
        long difference = TimeRange.Start.Ticks - previousEnd.Ticks;
        return difference is >= -TimestampQuantizationToleranceTicks and <= TimestampQuantizationToleranceTicks;
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
