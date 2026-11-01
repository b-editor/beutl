using System.Runtime.CompilerServices;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Threading;

namespace Beutl.Audio;

public sealed partial class SourceSound : IThumbnailsProvider
{
    // Waveforms are composed on one thread. Bound the strips, including decoder initialization,
    // so scrolling through short clips does not keep a decoder open for every visible element.
    internal static ThumbnailStripGate WaveformGate { get; } = new(2);

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
        return ThumbnailsCacheKey.Compute(
            CoreSerializer.SerializeToJsonObject(this),
            ["Source", "OffsetPosition", "Gain", "Pan", "Speed", "Effect"]);
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
        if (!await WaveformGate.TryEnterAsync(cancellationToken))
            yield break;

        try
        {
            await foreach (var chunk in GetWaveformChunksCoreAsync(
                               chunkCount, samplesPerChunk, cacheService, cancellationToken))
            {
                yield return chunk;
            }
        }
        finally
        {
            WaveformGate.Release();
        }
    }

    private async IAsyncEnumerable<WaveformChunk> GetWaveformChunksCoreAsync(
        int chunkCount,
        int samplesPerChunk,
        IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var resource = await ComposeThread.Dispatcher.InvokeAsync(
            () => ToResource(CompositionContext.Default), DispatchPriority.Low, cancellationToken);
        if (resource.Source == null)
            yield break;

        if (chunkCount <= 0 || samplesPerChunk <= 0)
            yield break;

        if (resource.Source.Duration <= MediaTime.Zero)
            yield break;

        var duration = TimeRange.Value.Duration;

        // TimeRange.Duration is unnormalized; a non-positive span has no waveform and would drive
        // totalSamples negative (GetWaveformChunkSamplePosition then throws). Bail out instead.
        if (duration <= MediaTime.Zero)
            yield break;

        int sampleRate = resource.Source.SampleRate;
        long totalSamples = duration.ToSamples(sampleRate, MidpointRounding.ToZero);

        string? cacheKey = cacheService != null ? GetThumbnailsCacheKey() : null;
        double chunkDurationSecs = duration.ToSeconds() / chunkCount;
        var cacheThreshold = MediaTime.FromSeconds(chunkDurationSecs * 0.5);

        using var composer = new Composer { SampleRate = sampleRate };
        var frame = new CompositionFrame(
            [resource],
            TimeRange.Value,
            default,
            new CompositionEligibility([resource.RequireOriginal()]));

        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            long startSample = GetWaveformChunkSamplePosition(chunkIndex, totalSamples, chunkCount);
            long endSample = GetWaveformChunkSamplePosition(chunkIndex + 1, totalSamples, chunkCount);
            long fullSpan = endSample - startSample;
            // Long clips are approximated: capping at samplesPerChunk resets the effect state per chunk.
            int sampleCount = (int)Math.Min(fullSpan, samplesPerChunk);
            var chunkTime = MediaTime.FromSamples(startSample, sampleRate);
            MediaTime startTime = TimeRange.Value.Start + chunkTime;
            MediaTime durationTime = AudioProcessContext.GetDurationForSampleCount(sampleCount, sampleRate);

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

                (float minValue, float maxValue) = MeasureMonoRange(buffer, sampleCount);
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

    // The extremes of the left/right average over at most sampleCount samples.
    private static (float Min, float Max) MeasureMonoRange(AudioBuffer buffer, int sampleCount)
    {
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

        return (minValue, maxValue);
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
}
