using Beutl.Engine;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.TimelineTab.Services;

internal static class SilenceWaveformAnalysis
{
    public static IReadOnlyList<IThumbnailsProvider> FindAudioProviders(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var providers = new List<IThumbnailsProvider>();
        foreach (EngineObject obj in element.Objects)
        {
            if (obj.IsEnabled
                && obj is IThumbnailsProvider { ThumbnailsKind: ThumbnailsKind.Audio } provider)
            {
                providers.Add(provider);
            }
        }

        return providers;
    }

    public static async Task<IReadOnlyList<WaveformChunk>> CollectConservativeChunksAsync(
        IReadOnlyList<IThumbnailsProvider> providers,
        int chunkCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (chunkCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkCount), chunkCount, "chunkCount must be positive.");

        cancellationToken.ThrowIfCancellationRequested();

        var combinedPeaks = new float[chunkCount];
        var hasCombinedPeak = new bool[chunkCount];
        foreach (IThumbnailsProvider provider in providers)
        {
            var providerPeaks = new float[chunkCount];
            var hasProviderPeak = new bool[chunkCount];

            // Silence-based deletion needs every sample in the interval. Preview waveform caches
            // may contain prefix-only chunks, so request the complete span and bypass that cache.
            await foreach (WaveformChunk chunk in provider.GetWaveformChunksAsync(
                chunkCount,
                int.MaxValue,
                cacheService: null,
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((uint)chunk.Index >= (uint)chunkCount)
                    continue;

                float peak = Math.Max(Math.Abs(chunk.MinValue), Math.Abs(chunk.MaxValue));
                if (!float.IsFinite(peak))
                    peak = float.PositiveInfinity;

                providerPeaks[chunk.Index] = Math.Max(providerPeaks[chunk.Index], peak);
                hasProviderPeak[chunk.Index] = true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < chunkCount; i++)
            {
                if (!hasProviderPeak[i])
                    continue;

                combinedPeaks[i] = AddPeaks(combinedPeaks[i], providerPeaks[i]);
                hasCombinedPeak[i] = true;
            }
        }

        var result = new List<WaveformChunk>(chunkCount);
        for (int i = 0; i < chunkCount; i++)
        {
            if (hasCombinedPeak[i])
            {
                float peak = combinedPeaks[i];
                result.Add(new WaveformChunk(i, -peak, peak));
            }
        }

        return result;
    }

    private static float AddPeaks(float left, float right)
    {
        double sum = (double)left + right;
        return double.IsFinite(sum) && sum <= float.MaxValue
            ? (float)sum
            : float.PositiveInfinity;
    }
}
