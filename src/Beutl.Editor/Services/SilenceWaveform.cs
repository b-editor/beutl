using System.Runtime.CompilerServices;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Threading;

namespace Beutl.Editor.Services;

/// <summary>Full-coverage, channel-preserving peak envelopes for silence analysis.</summary>
internal static class SilenceWaveform
{
    public static async IAsyncEnumerable<WaveformChunk> GetChunksAsync(
        Scene scene, Element element, int chunkCount, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkCount);
        var range = element.Range;
        int sampleRate = scene.FindHierarchicalParent<Project>().GetSampleRate();
        if (range.Duration <= MediaTime.Zero) yield break;
        long totalSamples = range.Duration.ToSamples(sampleRate, MidpointRounding.ToZero);
        if (totalSamples < chunkCount) yield break;

        using var composer = new Composer { SampleRate = sampleRate };
        using var compositor = new SceneCompositor(scene) { ForceOriginalSource = true };
        // Compose about one second per dispatch, then reduce it into small envelope bins. This
        // avoids tens of thousands of dispatcher hops and preserves state across contiguous blocks.
        int batchSize = Math.Max(1, (int)Math.Min(chunkCount, sampleRate * (long)chunkCount / totalSamples));
        for (int first = 0; first < chunkCount; first += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int from = first;
            int to = Math.Min(chunkCount, first + batchSize);
            WaveformChunk[] chunks = await ComposeThread.Dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                long start = Position(from);
                long end = Position(to);
                int count = checked((int)(end - start));
                var window = new TimeRange(range.Start + MediaTime.FromSamples(start, sampleRate),
                    AudioProcessContext.GetDurationForSampleCount(count, sampleRate));
                using var reads = new AudioReadScope();
                using var buffer = composer.Compose(window, compositor.EvaluateElementAudio(element, window));
                if (reads.HasFailures) throw new InvalidDataException("Audio could not be read.");
                var result = new WaveformChunk[to - from];
                for (int i = from; i < to; i++)
                {
                    int begin = checked((int)(Position(i) - start));
                    int finish = checked((int)(Position(i + 1) - start));
                    float min = float.PositiveInfinity;
                    float max = float.NegativeInfinity;
                    if (buffer != null && buffer.SampleCount >= finish)
                    {
                        // Averaging channels would label audible opposite-phase stereo as silence.
                        for (int channel = 0; channel < buffer.ChannelCount; channel++)
                        {
                            var data = buffer.GetChannelData(channel);
                            for (int sample = begin; sample < finish; sample++)
                            {
                                min = Math.Min(min, data[sample]);
                                max = Math.Max(max, data[sample]);
                            }
                        }
                    }
                    result[i - from] = new WaveformChunk(i, min, max);
                }
                return result;
            }, DispatchPriority.Low, cancellationToken).ConfigureAwait(false);
            foreach (var chunk in chunks) yield return chunk;
        }

        long Position(int index) => (long)((Int128)index * totalSamples / chunkCount);
    }
}
