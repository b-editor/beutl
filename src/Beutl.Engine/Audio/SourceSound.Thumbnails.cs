using System.Runtime.CompilerServices;
using Beutl.Animation;
using Beutl.Audio.Composing;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
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
        string? key = ThumbnailsCacheKey.Compute(
            CoreSerializer.SerializeToJsonObject(this),
            ["Source", "ChannelMap", "OffsetPosition", "Gain", "Pan", "Speed", "Effect"]);
        return key != null && RequiresResampledStartPreview ? $"{key}|start:{TimeRange.Value.Start.Flicks}" : key;
    }

    // A slip moves the source offset while the clip's processing still runs on the same clock.
    internal bool RequiresResampledSlipPreview
        => Properties.Any(AnimationSampler.IsAnimated) || HasEnabledEffect(Effect.Value);

    internal bool RequiresResampledTrimPreview
        => Properties.Any(property => property.GetKeyFrameAnimation() is { KeyFrames.Count: > 0, UseGlobalClock.Value: false })
            || HasEnabledEffect(Effect.Value);

    internal bool RequiresResampledStartPreview
        => Properties.Any(AnimationSampler.DependsOnTimelineStart) || HasEnabledEffect(Effect.Value);

    private static bool HasEnabledEffect(AudioEffect? effect)
        => effect is { IsEnabled.Value: true }
            && (effect is not AudioEffectGroup group || group.Children.Any(HasEnabledEffect));

    public async IAsyncEnumerable<(int Index, int Count, Bitmap Thumbnail)> GetThumbnailStripAsync(
        FontManager fonts,
        int maxWidth,
        int maxHeight,
        IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        int startIndex = 0,
        int endIndex = -1, DecoderRegistry? decoders = null)
    {
        await Task.CompletedTask;
        yield break;
    }

    public async IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(
        int chunkCount,
        int samplesPerChunk,
        IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken = default, DecoderRegistry? decoders = null)
    {
        if (!await WaveformGate.TryEnterAsync(cancellationToken))
            yield break;

        try
        {
            await foreach (var chunk in GetWaveformChunksCoreAsync(
                               chunkCount, samplesPerChunk, cacheService, cancellationToken, decoders))
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
        [EnumeratorCancellation] CancellationToken cancellationToken, DecoderRegistry? decoders)
    {
        using var resource = await ComposeThread.Dispatcher.InvokeAsync(
            () => ToResource(new CompositionContext(MediaTime.Zero) { Decoders = decoders }), DispatchPriority.Low, cancellationToken);
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

    internal async IAsyncEnumerable<WaveformChunk> GetPreviewWaveformChunksAsync(
        int baseChunkCount,
        int samplesPerChunk,
        IReadOnlyList<int> indices,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        DecoderRegistry? decoders = null,
        MediaTime offsetDelta = default,
        TimeRange? previewLayout = null,
        MediaTime startDelta = default)
    {
        if (indices.Count == 0 || baseChunkCount <= 0 || samplesPerChunk <= 0
            || !await WaveformGate.TryEnterAsync(cancellationToken))
            yield break;

        try
        {
            using var resource = await ComposeThread.Dispatcher.InvokeAsync(
                () => ToResource(new CompositionContext(MediaTime.Zero) { Decoders = decoders }),
                DispatchPriority.Low, cancellationToken);
            if (resource.Source is not { SampleRate: > 0 } source || source.Duration <= MediaTime.Zero)
                yield break;

            TimeRange clipRange = TimeRange.Value;
            TimeRange layout = previewLayout ?? new TimeRange(MediaTime.Zero, clipRange.Duration);
            if (layout.Duration <= MediaTime.Zero)
                yield break;

            int sampleRate = source.SampleRate;
            long totalSamples = layout.Duration.ToSamples(sampleRate, MidpointRounding.ToZero);
            long origin = layout.Start.ToSamples(sampleRate, MidpointRounding.ToZero);
            using var audioContext = new AudioContext(sampleRate, AudioChannelLayout.Stereo);
            var (output, finiteEndSample) = await ComposeThread.Dispatcher.InvokeAsync(() =>
            {
                var output = ComposeForWaveformPreview(audioContext, resource, offsetDelta);
                if (startDelta != MediaTime.Zero)
                {
                    foreach (IAudioTimeMappingNode node in audioContext.Nodes.OfType<IAudioTimeMappingNode>())
                        node.SetPreviewStartDelta(startDelta);
                }
                return (output, output?.GetFiniteSourceEndSample(sampleRate));
            }, DispatchPriority.Low, cancellationToken);
            var sampler = new AnimationSampler { PreviewStartDelta = startDelta };

            // Sparse requests have different processing history from a normal, sequential waveform.
            foreach (int index in indices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long start = GetSamplePosition(index);
                int count = (int)Math.Min(GetSamplePosition(index + 1) - start, samplesPerChunk);
                start += origin;
                if (count <= 0 || output == null || finiteEndSample is { } end && start >= end)
                {
                    yield return new WaveformChunk(index, 0, 0);
                    continue;
                }

                var range = new TimeRange(
                    MediaTime.FromSamples(start, sampleRate),
                    AudioProcessContext.GetDurationForSampleCount(count, sampleRate));
                yield return await ComposeThread.Dispatcher.InvokeAsync(() =>
                {
                    using var buffer = output.Process(new AudioProcessContext(
                        range, sampleRate, sampler, range.AddStart(clipRange.Start + startDelta)));
                    for (int ch = 0; ch < buffer.ChannelCount; ch++)
                    {
                        var data = buffer.GetChannelData(ch);
                        AudioMath.ApplyLimiter(data);
                        AudioMath.ClampToFullScale(data);
                    }

                    var (min, max) = MeasureMonoRange(buffer, count);
                    return new WaveformChunk(index, min, max);
                }, DispatchPriority.Low, cancellationToken);
            }

            long GetSamplePosition(int index)
                => index >= 0 && index <= baseChunkCount
                    ? GetWaveformChunkSamplePosition(index, totalSamples, baseChunkCount)
                    : (long)Math.Floor((double)index * totalSamples / baseChunkCount);
        }
        finally
        {
            WaveformGate.Release();
        }
    }

    // The extremes of the left/right average over at most sampleCount samples.
    private static (float Min, float Max) MeasureMonoRange(AudioBuffer buffer, int sampleCount)
    {
        int count = Math.Min(sampleCount, buffer.SampleCount);
        if (count <= 0)
            return (0f, 0f);

        var firstChannel = buffer.GetChannelData(0);
        var secondChannel = buffer.GetChannelData(buffer.ChannelCount > 1 ? 1 : 0);
        float minValue = float.MaxValue;
        float maxValue = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            float monoValue = (firstChannel[i] + secondChannel[i]) * 0.5f;
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
