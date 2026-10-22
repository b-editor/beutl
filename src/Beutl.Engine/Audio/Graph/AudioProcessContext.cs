using System;
using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Graph;

public sealed class AudioProcessContext
{
    // One TimeSpan tick, the most that independently rounded boundaries of files differ by.
    internal static readonly MediaTime TimestampQuantizationTolerance = MediaTime.FromTicks(1, MidpointRounding.ToPositiveInfinity);

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

    // Terminal live-input boundary in the current node's timeline. Clip nodes establish it;
    // time-mapping nodes translate it before forwarding contexts to their inputs.
    internal MediaTime? ProcessEndTime { get; init; }

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
    /// Always rounds up so durations between sample boundaries never under-allocate.
    /// Both per-node audio paths and the silence fallback in <see cref="Composing.Composer"/> must route through
    /// this helper to stay in sync; replacing it with truncation will desynchronise mix and silent buffers by one sample.
    /// </remarks>
    public static int GetSampleCount(TimeRange range, int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        if (range.Duration < MediaTime.Zero)
            throw new ArgumentOutOfRangeException(nameof(range), $"Duration must be non-negative; was {range.Duration}.");

        long samples = range.Duration.ToSamples(sampleRate, MidpointRounding.ToPositiveInfinity);
        if (samples > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(range), $"Sample count {samples} exceeds Int32.MaxValue at sampleRate={sampleRate}.");

        return (int)samples;
    }

    /// <summary>
    /// Returns a representable duration whose ceiling-based sample count is exactly
    /// <paramref name="sampleCount"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleCount"/> is negative, <paramref name="sampleRate"/> is not positive, or
    /// the requested sample count cannot be represented at the given rate with flick precision.
    /// </exception>
    public static MediaTime GetDurationForSampleCount(int sampleCount, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        if (sampleCount == 0)
            return MediaTime.Zero;

        var duration = MediaTime.FromSamples(sampleCount, sampleRate);
        int roundTripped = GetSampleCount(new TimeRange(MediaTime.Zero, duration), sampleRate);
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
    /// Independently rounded boundaries read from files may differ by one tick. A gap or overlap of up to
    /// one tick is contiguous; more is a seek/edit boundary. Stateful nodes must use this helper so a
    /// chain resets consistently.
    /// </remarks>
    public bool ContinuesFrom(MediaTime previousEnd)
    {
        MediaTime difference = TimeRange.Start - previousEnd;
        return difference >= -TimestampQuantizationTolerance && difference <= TimestampQuantizationTolerance;
    }

    public MediaTime GetTimeForSample(int sampleIndex)
    {
        if (sampleIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(sampleIndex), "Sample index must be non-negative.");

        return TimeRange.Start + MediaTime.FromSamples(sampleIndex, SampleRate);
    }

    // The absolute time range of samples [startSample, startSample + sampleCount) in this block; nodes sample
    // their animated parameters one chunk at a time. GetTimeForSample already includes TimeRange.Start.
    internal TimeRange GetChunkRange(int startSample, int sampleCount)
    {
        var chunkStart = GetTimeForSample(startSample);
        var chunkEnd = GetTimeForSample(startSample + sampleCount);
        return new TimeRange(chunkStart, chunkEnd - chunkStart);
    }

    public int GetSampleForTime(MediaTime time)
    {
        var offset = time - TimeRange.Start;
        if (offset < MediaTime.Zero)
            return -1;

        return (int)offset.ToSamples(SampleRate, MidpointRounding.ToZero);
    }
}
