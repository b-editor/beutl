using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.Audio.Composing;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Threading;

namespace Beutl.Audio;

public sealed partial class SourceSound : IElementThumbnailsProvider
{
    private EventHandler? _thumbnailHandler;

    public ElementThumbnailsKind ThumbnailsKind => ElementThumbnailsKind.Audio;

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

    public async IAsyncEnumerable<(int Index, int Count, IBitmap Thumbnail)> GetThumbnailStripAsync(
        int maxWidth,
        int maxHeight,
        IElementThumbnailCacheService? cacheService,
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
        IElementThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var resource = ToResource(RenderContext.Default);
        if (resource.Source == null)
            yield break;

        if (chunkCount <= 0 || samplesPerChunk <= 0)
            yield break;

        if (resource.Source.Duration <= TimeSpan.Zero)
            yield break;

        var duration = TimeRange.Duration;

        int sampleRate = resource.Source.SampleRate;
        int totalSamples = (int)(duration.TotalSeconds * sampleRate);

        string? cacheKey = cacheService != null ? GetThumbnailsCacheKey() : null;
        double chunkDurationSecs = duration.TotalSeconds / chunkCount;
        var cacheThreshold = TimeSpan.FromSeconds(chunkDurationSecs * 0.5);

        using var composer = new Composer { SampleRate = sampleRate };
        var frame = new CompositionFrame([resource], TimeRange, default);

        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            int startSample = (int)((long)chunkIndex * totalSamples / chunkCount);
            int endSample = (int)((long)(chunkIndex + 1) * totalSamples / chunkCount);
            int sampleCount = Math.Min(endSample - startSample, samplesPerChunk);
            var chunkTime = TimeSpan.FromSeconds((double)startSample / sampleRate);
            TimeSpan startTime = TimeRange.Start + chunkTime;
            TimeSpan durationTime = TimeSpan.FromSeconds((double)sampleCount / sampleRate);

            if (sampleCount <= 0)
                continue;

            if (cacheKey != null
                && cacheService!.TryGetWaveform(cacheKey, chunkTime, cacheThreshold, out var cachedMin,
                    out var cachedMax))
            {
                yield return new WaveformChunk(chunkIndex, cachedMin, cachedMax);
                continue;
            }

            var chunk = await ComposeThread.Dispatcher.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return (WaveformChunk?)null;

                using var buffer = composer.Compose(new TimeRange(startTime, durationTime), frame);
                if (buffer == null || buffer.SampleCount == 0)
                    return null;

                var firstChannel = buffer.GetChannelData(0);
                var secondChannel = buffer.GetChannelData(1);

                float minValue = float.MaxValue;
                float maxValue = float.MinValue;

                for (int i = 0; i < buffer.SampleCount; i++)
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
                if (cacheKey != null)
                    cacheService!.SaveWaveform(cacheKey, chunkTime, chunk.Value.MinValue, chunk.Value.MaxValue);

                yield return chunk.Value;
            }
        }
    }
}
