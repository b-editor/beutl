using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Threading;

namespace Beutl.Audio;

public sealed partial class SourceSound : IThumbnailsProvider
{
    private EventHandler? _thumbnailHandler;

    public ThumbnailsKind ThumbnailsKind => ThumbnailsKind.Audio;

    public event EventHandler? ThumbnailsInvalidated;

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        _thumbnailHandler = (_, _) => ThumbnailsInvalidated?.Invoke(this, EventArgs.Empty);
        Edited += _thumbnailHandler;
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (_thumbnailHandler != null)
            Edited -= _thumbnailHandler;
        _thumbnailHandler = null;
    }

    public string? GetThumbnailsCacheKey()
    {
        var fullJson = CoreSerializer.SerializeToJsonObject(this);
        var cacheJson = new JsonObject();
        string[] targetProps = ["Source", "OffsetPosition", "Gain", "Speed", "Effect"];

        foreach (var prop in targetProps)
        {
            if (fullJson.TryGetPropertyValue(prop, out var node))
                cacheJson[prop] = node?.DeepClone();
        }

        if (fullJson.TryGetPropertyValue("Animations", out var anims) && anims is JsonObject animObj)
        {
            var filtered = new JsonObject();
            foreach (var prop in targetProps)
                if (animObj.TryGetPropertyValue(prop, out var n))
                    filtered[prop] = n?.DeepClone();
            if (filtered.Count > 0) cacheJson["Animations"] = filtered;
        }

        if (fullJson.TryGetPropertyValue("Expressions", out var exprs) && exprs is JsonObject exprObj)
        {
            var filtered = new JsonObject();
            foreach (var prop in targetProps)
                if (exprObj.TryGetPropertyValue(prop, out var n))
                    filtered[prop] = n?.DeepClone();
            if (filtered.Count > 0) cacheJson["Expressions"] = filtered;
        }

        var jsonStr = cacheJson.ToJsonString();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(jsonStr));
        return Convert.ToHexString(hash);
    }

    public async IAsyncEnumerable<(int Index, int Count, Bitmap Thumbnail)> GetThumbnailStripAsync(
        int maxWidth,
        int maxHeight,
        IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        int startIndex = 0,
        int endIndex = -1)
    {
        await Task.CompletedTask;
        yield break;
    }

    public async IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(
        int chunkCount,
        int samplesPerChunk,
        IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Waveform generation is preview-only auxiliary work and must not alter retained frame state in nested
        // sound/effect resources.
        using var resource = ToResource(new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Preview,
            RenderPullPurpose.Auxiliary));
        if (resource.Source == null)
            yield break;

        if (chunkCount <= 0 || samplesPerChunk <= 0)
            yield break;

        if (resource.Source.Duration <= TimeSpan.Zero)
            yield break;

        var duration = TimeRange.Duration;

        // TimeRange.Duration is unnormalized; a non-positive span has no waveform and would drive
        // totalSamples negative (GetWaveformChunkSamplePosition then throws). Bail out instead.
        if (duration <= TimeSpan.Zero)
            yield break;

        int sampleRate = resource.Source.SampleRate;
        long totalSamples = AudioMath.TimeToSampleIndex(duration, sampleRate);

        string? cacheKey = cacheService != null ? GetThumbnailsCacheKey() : null;
        double chunkDurationSecs = duration.TotalSeconds / chunkCount;
        var cacheThreshold = TimeSpan.FromSeconds(chunkDurationSecs * 0.5);

        using var composer = new Composer(RenderIntent.Preview) { SampleRate = sampleRate };
        var frame = new CompositionFrame(
            [resource],
            TimeRange,
            default,
            RenderIntent.Preview,
            RenderPullPurpose.Auxiliary);

        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            long startSample = GetWaveformChunkSamplePosition(chunkIndex, totalSamples, chunkCount);
            long endSample = GetWaveformChunkSamplePosition(chunkIndex + 1, totalSamples, chunkCount);
            long fullSpan = endSample - startSample;
            // samplesPerChunk caps the work per chunk. Compose ranges stay tick-contiguous — so a
            // stateful effect like the limiter carries state across the strip — only when
            // fullSpan <= samplesPerChunk (short or zoomed-in clips). For longer clips the waveform
            // is a sparse approximation and the effect restarts per chunk: a deliberate quality
            // trade-off for bounded per-chunk cost, not a correctness guarantee.
            int sampleCount = (int)Math.Min(fullSpan, samplesPerChunk);
            var chunkTime = TimeSpan.FromSeconds((double)startSample / sampleRate);
            TimeSpan startTime = TimeRange.Start + chunkTime;
            TimeSpan durationTime = GetWaveformChunkDuration(sampleCount, sampleRate);

            if (sampleCount <= 0)
                continue;

            float cachedMin = 0f;
            float cachedMax = 0f;
            bool cacheHit = cacheKey != null
                && cacheService!.TryGetWaveform(
                    cacheKey, chunkTime, cacheThreshold, out cachedMin, out cachedMax);

            var chunk = await ComposeThread.Dispatcher.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return (WaveformChunk?)null;

                using var buffer = composer.Compose(new TimeRange(startTime, durationTime), frame);
                if (buffer == null || buffer.SampleCount == 0)
                    return null;

                // Compose must run on every chunk even when its min/max is cached: skipping it would
                // leave the next cache miss starting from reset state, diverging from a cold run.
                if (cacheHit)
                    return new WaveformChunk(chunkIndex, cachedMin, cachedMax);

                var firstChannel = buffer.GetChannelData(0);
                var secondChannel = buffer.GetChannelData(1);

                float minValue = float.MaxValue;
                float maxValue = float.MinValue;

                for (int i = 0; i < Math.Min(sampleCount, buffer.SampleCount); i++)
                {
                    float left = firstChannel[i];
                    float right = secondChannel[i];

                    float monoValue = (left + right) * 0.5f;
                    minValue = Math.Min(minValue, monoValue);
                    maxValue = Math.Max(maxValue, monoValue);
                }

                return new WaveformChunk(chunkIndex, minValue, maxValue);
            }, DispatchPriority.Low, cancellationToken);

            if (chunk.HasValue)
            {
                if (cacheKey != null && !cacheHit)
                    cacheService!.SaveWaveform(cacheKey, chunkTime, chunk.Value.MinValue, chunk.Value.MaxValue);

                yield return chunk.Value;
            }
        }
    }

    internal static long GetWaveformChunkSamplePosition(int chunkIndex, long totalSamples, int chunkCount)
    {
        if (chunkIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        if (totalSamples < 0)
            throw new ArgumentOutOfRangeException(nameof(totalSamples));
        if (chunkCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkCount));
        if (chunkIndex > chunkCount)
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));

        long quotient = totalSamples / chunkCount;
        long remainder = totalSamples % chunkCount;
        return chunkIndex * quotient + chunkIndex * remainder / chunkCount;
    }

    internal static TimeSpan GetWaveformChunkDuration(int sampleCount, int sampleRate)
    {
        if (sampleCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        // Floor the tick count: TimeSpan.FromSeconds rounds to nearest, and rounding up would let
        // AudioProcessContext.GetSampleCount's ceiling request one extra sample. Flooring maps a
        // positive duration back to exactly sampleCount samples.
        long ticks = (long)((decimal)sampleCount * TimeSpan.TicksPerSecond / sampleRate);
        return TimeSpan.FromTicks(ticks);
    }
}
