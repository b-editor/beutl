using Beutl.Engine;

namespace Beutl.Audio;

// Start/End are 0-based times from the start of the analyzed waveform (chunk 0), NOT scene-timeline
// times; the caller offsets by the source's timeline start. Padding is already subtracted off both ends.
public readonly record struct SilenceRegion(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// Tuning parameters for <see cref="SilenceDetector.Detect"/>: <c>ThresholdDb</c> is the peak
/// amplitude in dBFS at or below which a chunk counts as silent; <c>MinSilenceDuration</c> is the
/// shortest run that becomes a region; <c>Padding</c> is shrunk off both ends of every run.
/// </summary>
public readonly record struct SilenceDetectionOptions(
    double ThresholdDb,
    TimeSpan MinSilenceDuration,
    TimeSpan Padding);

// Pure silence-detection over WaveformChunk min/max pairs (no UI, no I/O). Missing chunk
// indices — the provider skips empty ranges near the end — are treated as silent so a
// truncated tail still reports as one region instead of fragmenting the run.
public static class SilenceDetector
{
    public static IReadOnlyList<SilenceRegion> Detect(
        IReadOnlyList<WaveformChunk> chunks,
        TimeSpan totalDuration,
        int chunkCount,
        SilenceDetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunkCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkCount), chunkCount, "chunkCount must be positive.");
        if (options.MinSilenceDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.MinSilenceDuration, "MinSilenceDuration must be non-negative.");
        if (options.Padding < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.Padding, "Padding must be non-negative.");
        if (totalDuration <= TimeSpan.Zero)
            return [];
        if (chunks.Count == 0)
            return [];

        double thresholdLinear = Math.Pow(10.0, options.ThresholdDb / 20.0);
        TimeSpan chunkDuration = TimeSpan.FromTicks(totalDuration.Ticks / chunkCount);
        if (chunkDuration <= TimeSpan.Zero)
            return [];

        var silentByIndex = new Dictionary<int, bool>(chunks.Count);
        foreach (WaveformChunk chunk in chunks)
        {
            float peak = Math.Max(Math.Abs(chunk.MinValue), Math.Abs(chunk.MaxValue));
            silentByIndex[chunk.Index] = peak <= thresholdLinear;
        }

        var regions = new List<SilenceRegion>();
        int runStart = -1;
        for (int i = 0; i < chunkCount; i++)
        {
            bool silent = !silentByIndex.TryGetValue(i, out bool isSilent) || isSilent;
            if (silent)
            {
                runStart = runStart < 0 ? i : runStart;
            }
            else if (runStart >= 0)
            {
                AddIfValid(regions, runStart, i - 1, chunkDuration, options);
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            AddIfValid(regions, runStart, chunkCount - 1, chunkDuration, options);
        }

        return regions;
    }

    private static void AddIfValid(
        List<SilenceRegion> regions,
        int startChunk,
        int endChunk,
        TimeSpan chunkDuration,
        SilenceDetectionOptions options)
    {
        long rawStartTicks = (long)startChunk * chunkDuration.Ticks;
        long rawEndTicks = (long)(endChunk + 1) * chunkDuration.Ticks;
        TimeSpan rawStart = TimeSpan.FromTicks(rawStartTicks);
        TimeSpan rawEnd = TimeSpan.FromTicks(rawEndTicks);

        if (rawEnd - rawStart < options.MinSilenceDuration)
            return;

        TimeSpan paddedStart = rawStart + options.Padding;
        TimeSpan paddedEnd = rawEnd - options.Padding;
        if (paddedEnd <= paddedStart)
            return;

        regions.Add(new SilenceRegion(paddedStart, paddedEnd));
    }
}
