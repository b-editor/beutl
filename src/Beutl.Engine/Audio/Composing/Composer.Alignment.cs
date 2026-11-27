using Beutl.Audio.Graph;
using Beutl.Composition;

namespace Beutl.Audio.Composing;

public partial class Composer
{
    // Build the graphs for the playback range without advancing any DSP state. A stable
    // scene-wide target keeps the output cursor valid even when the slowest sound comes and goes.
    internal int GetPlaybackLatency(CompositionFrame frame, bool forEmbeddedScene = false)
    {
        int minimum = int.MaxValue;
        int maximum = 0;
        foreach (var resource in frame.Objects)
        {
            if (resource is not Sound.Resource sound)
                continue;

            foreach (int? latency in GetFixedLatencies(GetOrUpdateEntry(sound)))
            {
                if (latency is { } fixedLatency)
                {
                    minimum = Math.Min(minimum, fixedLatency);
                    maximum = Math.Max(maximum, fixedLatency);
                }
                else if (forEmbeddedScene)
                {
                    return 0;
                }
            }
        }

        // An embed removes the offset added by its scene mix when all outputs can
        // be aligned. Other embeds keep their original timing and drain contracts.
        return forEmbeddedScene && minimum >= maximum ? 0 : maximum;
    }

    private int GetAlignmentTarget()
    {
        if (_currentEntry.Count == 0)
            return _alignmentLatency;

        IEnumerable<AudioNodeEntry> entries = _currentEntry;
        if (_previousRange is { } previous)
            entries = entries.Concat(_previousEntry.Where(entry => CanDrainEndedEntry(entry, previous.End)));

        // Raw callers provide only the current window. Keep the largest latency encountered
        // while mixing so a branch does not jump forward when a slower branch ends.
        if (entries.Sum(entry => entry.OutputNodes?.Length ?? 0) <= 1)
            return _alignmentLatency;
        return Math.Max(_alignmentLatency, entries.Max(GetEntryFixedLatency));
    }

    private int GetEntryFixedLatency(AudioNodeEntry entry)
    {
        int max = 0;
        foreach (int? latency in GetFixedLatencies(entry))
            max = Math.Max(max, latency.GetValueOrDefault());
        return max;
    }

    private int?[] GetFixedLatencies(AudioNodeEntry entry)
        => entry.FixedLatencies ??= entry.OutputNodes is { } outputNodes
            ? Array.ConvertAll(outputNodes, node => node.GetFixedLatencySamples(SampleRate))
            : [];

    private AudioDelayLine? GetOutputDelay(AudioNodeEntry entry, AudioNode node, int? fixedLatency, bool isPlayback)
    {
        if (_alignmentLatency == 0)
            return null;

        // A non-fixed output stays at its original timeline position. During playback it
        // only receives the common offset that the sequential reader will discard.
        int delaySamples = fixedLatency is { } latency && latency <= _alignmentLatency
            ? _alignmentLatency - latency
            : isPlayback ? _alignmentLatency : 0;
        if (delaySamples == 0)
            return null;

        var delays = entry.Delays ??= new(ReferenceEqualityComparer.Instance);
        if (delays.TryGetValue(node, out AudioDelayLine? delay))
        {
            if (delay.DelaySamples == delaySamples)
                return delay;
            if (delay.DelaySamples < delaySamples)
                return delays[node] = delay.Grow(delaySamples);
        }

        return delays[node] = new AudioDelayLine(SampleRate, ChannelLayout.ChannelCount, delaySamples);
    }

    private static int IncludeOutputDelay(AudioNodeEntry entry, AudioNode node, int latency, int sampleRate)
    {
        if (entry.Delays is not { } delays || !delays.TryGetValue(node, out AudioDelayLine? delay))
            return latency;

        int held = latency > 0 ? delay.DelaySamples : delay.PendingSamples;
        return AudioLatency.SaturatingAdd(latency, AudioLatency.ScaleSampleCount(held, delay.SampleRate, sampleRate));
    }
}
