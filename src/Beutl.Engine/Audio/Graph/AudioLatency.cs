using Beutl.Media;

namespace Beutl.Audio.Graph;

// Latency reports use int.MaxValue for an unbounded or saturated budget, so every fold over them saturates
// instead of overflowing, and a negative report is a broken node rather than a value to clamp.
internal static class AudioLatency
{
    public static void ThrowIfNegative(AudioNode node, int latency, string description)
    {
        if (latency < 0)
        {
            throw new InvalidOperationException(
                $"{node.GetType().Name} returned negative {description} {latency}.");
        }
    }

    public static int SaturatingAdd(int first, int second)
    {
        if (first == int.MaxValue || second == int.MaxValue)
            return int.MaxValue;

        long sum = (long)first + second;
        return sum >= int.MaxValue ? int.MaxValue : (int)sum;
    }

    public static int ScaleSampleCount(int sampleCount, int sourceSampleRate, int destinationSampleRate)
    {
        if (sampleCount == int.MaxValue || sourceSampleRate == destinationSampleRate)
            return sampleCount;

        double scaled = sampleCount * (double)destinationSampleRate / sourceSampleRate;
        return scaled >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(scaled);
    }

    // Flushes the first drainSamples of a node's tail into a zero-padded buffer of windowSamples, so a tail
    // that ends inside the block still mixes as a full-length buffer.
    public static AudioBuffer FlushIntoWindow(
        AudioNode node,
        AudioProcessContext context,
        int drainSamples,
        int windowSamples)
    {
        AudioProcessContext drainContext = drainSamples == windowSamples
            ? context
            : new AudioProcessContext(
                new TimeRange(
                    context.TimeRange.Start,
                    AudioProcessContext.GetDurationForSampleCount(drainSamples, context.SampleRate)),
                context.SampleRate,
                context.AnimationSampler,
                context.OriginalTimeRange);

        using AudioBuffer drained = node.Flush(drainContext);
        var output = new AudioBuffer(drained.SampleRate, drained.ChannelCount, windowSamples);
        try
        {
            int copyCount = Math.Min(drained.SampleCount, windowSamples);
            if (copyCount > 0)
            {
                drained.CopyTo(output, 0, copyCount);
            }

            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }
}
