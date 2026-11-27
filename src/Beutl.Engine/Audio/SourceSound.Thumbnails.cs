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
using Beutl.Media.Proxy;
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
        string key = ThumbnailsCacheKey.Compute(
            CoreSerializer.SerializeToJsonObject(this),
            ["Source", "ChannelMap", "OffsetPosition", "Gain", "Pan", "Speed", "Effect"]);
        return RequiresResampledRangePreview(startChanges: true)
            ? $"{key}|start:{TimeRange.Value.Start.Flicks}|duration:{TimeRange.Value.Duration.Flicks}"
            : key;
    }

    // A slip moves the source offset while the clip's processing still runs on the same clock.
    internal bool RequiresResampledSlipPreview
        => Properties.Any(AnimationSampler.IsAnimated) || HasEnabledEffect(Effect.Value);

    internal bool RequiresResampledTrimPreview
        => Properties.Any(property => AnimationSampler.RequiresPreviewResampling(property, keyFrameGlobalClock: false))
            || HasEnabledEffect(Effect.Value);

    internal bool RequiresResampledRangePreview(bool startChanges)
        => Properties.Any(property => AnimationSampler.RequiresPreviewResampling(property, startChanges ? true : null))
            || HasEnabledEffect(Effect.Value);

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

    public IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(
        int chunkCount, int samplesPerChunk, IThumbnailCacheService? cacheService,
        CancellationToken cancellationToken = default, DecoderRegistry? decoders = null)
        => GetWaveformChunksAsync(chunkCount, samplesPerChunk, cacheService, null, cancellationToken, decoders);

    internal async IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(
        int chunkCount, int samplesPerChunk, IThumbnailCacheService? cacheService, IReadOnlyList<int>? indices,
        [EnumeratorCancellation] CancellationToken cancellationToken, DecoderRegistry? decoders)
    {
        TimeRange range = TimeRange.Value;
        if (chunkCount <= 0 || samplesPerChunk <= 0 || range.Duration <= MediaTime.Zero
            || indices is { Count: 0 } || !await WaveformGate.TryEnterAsync(cancellationToken).ConfigureAwait(false))
            yield break;

        try
        {
            bool stateful = HasEnabledEffect(Effect.Value) || Speed.Value != 100 || AnimationSampler.IsAnimated(Speed);
            bool canCache = cacheService != null && IsEnabled.Value && !Properties.Any(AnimationSampler.IsAnimated) && !HasEnabledEffect(Effect.Value);
            ProxyFingerprint? sourceFile = null;
            if (Source.Value is { HasUri: true } source
                && await Task.Run(() => ProxyFingerprint.TryFromFile(source.Uri.LocalPath, out var fingerprint)
                    ? fingerprint : (ProxyFingerprint?)null, cancellationToken).ConfigureAwait(false) is { } file)
                sourceFile = file;

            string? key = canCache && decoders != null && sourceFile is { } identity
                ? FormattableString.Invariant(
                    $"{GetThumbnailsCacheKey()}|{decoders.CacheId}|{decoders.Version}|{identity.FileSizeBytes}|{identity.MtimeUtc.Ticks}|{range.Duration.Flicks}|{chunkCount}|{samplesPerChunk}")
                : null;
            IEnumerable<int> requested = indices == null
                ? Enumerable.Range(0, chunkCount)
                : indices.Where(i => i >= 0 && i < chunkCount).Distinct().Order();
            var misses = new List<int>();
            foreach (int index in requested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (key != null && cacheService!.TryGetWaveform(key, new MediaTime(index), MediaTime.Zero, out float min, out float max))
                    yield return new WaveformChunk(index, min, max);
                else
                    misses.Add(index);
            }

            if (misses.Count == 0)
                yield break;

            if (decoders != null && sourceFile is { } fingerprint && CanUseAudioPeaks())
            {
                var mix = new AudioPeakReader.Mix(ChannelMap.Value, Gain.Value, Pan.Value);
                using var peaks = new AudioPeakReader(decoders, fingerprint, mix);
                var metadata = await Task.Run(peaks.GetMetadata, cancellationToken).ConfigureAwait(false);
                if (metadata is { Rate: > 0, Samples: > 0 and <= int.MaxValue })
                {
                    var remaining = new List<int>();
                    var layout = new TimeRange(MediaTime.Zero, range.Duration);
                    var offset = OffsetPosition.Value;
                    await foreach (var result in ReadPeakChunksAsync(peaks, layout, offset, metadata, chunkCount, misses, cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        if (result.Peak is { } chunk)
                        {
                            Save(chunk);
                            yield return chunk;
                        }
                        else
                            remaining.Add(result.Index);
                    }
                    misses = remaining;
                }
            }

            if (misses.Count == 0)
                yield break;

            using var resource = await ComposeThread.Dispatcher.InvokeAsync(
                () => ToResource(new CompositionContext(range.Start) { Decoders = decoders, IsThumbnail = true }),
                DispatchPriority.Low, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested
                || resource.Source is not { SampleRate: > 0, Duration: var sourceDuration } || sourceDuration <= MediaTime.Zero)
                yield break;

            int sampleRate = resource.Source.SampleRate;
            long totalSamples = range.Duration.ToSamples(sampleRate, MidpointRounding.ToZero);
            using var composer = new Composer { SampleRate = sampleRate };
            var frame = new CompositionFrame([resource], range, default, new CompositionEligibility([this]));
            var wanted = misses.ToHashSet();

            IEnumerable<int> work = stateful && totalSamples / chunkCount <= samplesPerChunk
                ? ContiguousWork()
                : misses;
            int largestChunk = (int)Math.Min(totalSamples / chunkCount, samplesPerChunk - 1) + 1;
            int batchSize = Math.Clamp(sampleRate / largestChunk, 1, 64);
            foreach (int[] batch in work.Chunk(batchSize))
            {
                var chunks = await ComposeThread.Dispatcher.InvokeAsync(() =>
                {
                    var values = new List<WaveformChunk>(batch.Length);
                    foreach (int index in batch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        long start = Position(index);
                        int count = (int)Math.Min(Position(index + 1) - start, samplesPerChunk);
                        if (count == 0)
                            continue;

                        MediaTime time = range.Start + MediaTime.FromSamples(start, sampleRate);
                        if (Source.HasExpression || ChannelMap.HasExpression)
                        {
                            bool versionBumped = false;
                            resource.Reconcile(this, new CompositionContext(time) { Decoders = decoders, IsThumbnail = true }, ref versionBumped);
                        }

                        using var reads = new AudioReadScope();
                        using var buffer = composer.Compose(
                            new TimeRange(time, AudioProcessContext.GetDurationForSampleCount(count, sampleRate)), frame);
                        if (wanted.Contains(index) && buffer is { SampleCount: > 0 })
                        {
                            var (min, max) = MeasureMonoRange(buffer, count);
                            var chunk = new WaveformChunk(index, min, max);
                            if (!reads.HasFailures)
                                Save(chunk);
                            values.Add(chunk);
                        }
                    }
                    return values;
                }, DispatchPriority.Low, cancellationToken).ConfigureAwait(false);

                foreach (var chunk in chunks)
                    yield return chunk;
            }

            long Position(int index) => GetWaveformChunkSamplePosition(index, totalSamples, chunkCount);

            IEnumerable<int> ContiguousWork()
            {
                // Stateful processing resumes from the most recent seek made by the normal waveform.
                // A cached or invisible slot still supplies history when a later slot needs processing.
                int last = -1;
                foreach (int index in misses)
                {
                    int first = index;
                    while (first > last + 1 && Position(first) - Position(first - 1) <= samplesPerChunk)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        first--;
                    }
                    for (int i = first; i <= index; i++)
                        yield return i;
                    last = index;
                }
            }

            void Save(WaveformChunk chunk)
            {
                if (key != null)
                    cacheService!.SaveWaveform(key, new MediaTime(chunk.Index), chunk.MinValue, chunk.MaxValue);
            }
        }
        finally
        {
            WaveformGate.Release();
        }
    }

    private bool CanUseAudioPeaks()
        // Non-unity speed goes through the sinc resampler. Its interpolated peaks and
        // seek history cannot be recovered from unresampled extrema.
        => IsEnabled.Value && Speed.Value == 100 && !HasEnabledEffect(Effect.Value)
            && !Properties.Any(AnimationSampler.IsAnimated);

    private static async IAsyncEnumerable<(int Index, WaveformChunk? Peak)> ReadPeakChunksAsync(
        AudioPeakReader peaks, TimeRange layout, MediaTime offset, (int Rate, long Samples) metadata,
        int chunkCount, IReadOnlyList<int> indices, [EnumeratorCancellation] CancellationToken ct)
    {
        var (sampleRate, sourceSamples) = metadata;
        long totalSamples = layout.Duration.ToSamples(sampleRate, MidpointRounding.ToZero);
        long origin = layout.Start.ToSamples(sampleRate, MidpointRounding.ToZero);
        int batchSize = Math.Clamp((int)(sampleRate / Math.Max(1, totalSamples / chunkCount)), 1, 64);
        foreach (int[] batch in indices.Chunk(batchSize))
        {
            var chunks = await Task.Run(() =>
            {
                var result = new (int Index, WaveformChunk? Peak)[batch.Length];
                for (int i = 0; i < batch.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    int index = batch[i];
                    long first = Position(index);
                    long length = Position(index + 1) - first;
                    MediaTime time = MediaTime.FromSamples(origin + first, sampleRate) + offset;
                    long start = time.ToSamples(sampleRate, MidpointRounding.ToZero);
                    var peak = length > 0 ? peaks.Measure(start, length, sampleRate, sourceSamples, ct) : (0f, 0f);
                    result[i] = (index, peak is { } value ? new WaveformChunk(index, value.Item1, value.Item2) : null);
                }
                return result;
            }, ct).ConfigureAwait(false);
            foreach (var chunk in chunks)
                yield return chunk;
        }

        long Position(int index)
            => index >= 0 && index <= chunkCount
                ? GetWaveformChunkSamplePosition(index, totalSamples, chunkCount)
                : (long)Math.Floor((double)index * totalSamples / chunkCount);
    }

    internal async IAsyncEnumerable<WaveformChunk> GetPreviewWaveformChunksAsync(
        int baseChunkCount,
        int samplesPerChunk,
        IReadOnlyList<int> indices,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        DecoderRegistry? decoders = null,
        MediaTime offsetDelta = default,
        TimeRange? previewLayout = null,
        MediaTime startDelta = default,
        MediaTime durationDelta = default)
    {
        if (indices.Count == 0 || baseChunkCount <= 0 || samplesPerChunk <= 0
            || !await WaveformGate.TryEnterAsync(cancellationToken))
            yield break;

        try
        {
            TimeRange clipRange = TimeRange.Value;
            TimeRange layout = previewLayout ?? new TimeRange(MediaTime.Zero, clipRange.Duration);
            if (layout.Duration <= MediaTime.Zero)
                yield break;

            if (decoders != null && CanUseAudioPeaks() && Source.Value is { HasUri: true } authored
                && await Task.Run(() => ProxyFingerprint.TryFromFile(authored.Uri.LocalPath, out var fingerprint)
                    ? fingerprint : (ProxyFingerprint?)null, cancellationToken).ConfigureAwait(false) is { } file)
            {
                using var peaks = new AudioPeakReader(decoders, file, new(ChannelMap.Value, Gain.Value, Pan.Value));
                var metadata = await Task.Run(peaks.GetMetadata, cancellationToken).ConfigureAwait(false);
                if (metadata is { Rate: > 0, Samples: > 0 and <= int.MaxValue })
                {
                    var remaining = new List<int>();
                    await foreach (var result in ReadPeakChunksAsync(peaks, layout, OffsetPosition.Value + offsetDelta,
                                       metadata, baseChunkCount, indices, cancellationToken).ConfigureAwait(false))
                    {
                        if (result.Peak is { } chunk)
                            yield return chunk;
                        else
                            remaining.Add(result.Index);
                    }
                    if (remaining.Count == 0)
                        yield break;
                    indices = remaining;
                }
            }

            using var resource = await ComposeThread.Dispatcher.InvokeAsync(
                () => ToResource(new CompositionContext(clipRange.Start + startDelta) { Decoders = decoders, IsThumbnail = true }),
                DispatchPriority.Low, cancellationToken).ConfigureAwait(false);
            if (resource.Source is not { SampleRate: > 0 } source || source.Duration <= MediaTime.Zero)
                yield break;

            int sampleRate = source.SampleRate;
            long totalSamples = layout.Duration.ToSamples(sampleRate, MidpointRounding.ToZero);
            long origin = layout.Start.ToSamples(sampleRate, MidpointRounding.ToZero);
            using var audioContext = new AudioContext(sampleRate, AudioChannelLayout.Stereo);
            var (output, finiteEndSample, historySamples) = await ComposeThread.Dispatcher.InvokeAsync(() =>
            {
                var output = ComposeForWaveformPreview(audioContext, resource, offsetDelta);
                if (startDelta != MediaTime.Zero)
                {
                    foreach (IAudioTimeMappingNode node in audioContext.Nodes.OfType<IAudioTimeMappingNode>())
                        node.SetPreviewStartDelta(startDelta);
                }
                long? requiredHistory = GetPreviewEffectHistory(Effect.Value, sampleRate);
                // Speed expressions and keyframed speeds depend on processing window boundaries.
                // The resampler carries state between windows, so replay the original slots from the last seek.
                if (Speed.Value != 100 || AnimationSampler.IsAnimated(Speed))
                    requiredHistory = null;

                return (output, output?.GetFiniteSourceEndSample(sampleRate), requiredHistory);
            }, DispatchPriority.Low, cancellationToken);
            var sampler = new AnimationSampler { PreviewStartDelta = startDelta, PreviewDurationDelta = durationDelta };

            foreach (var batch in BatchWindows())
            {
                var chunks = await ComposeThread.Dispatcher.InvokeAsync(() =>
                {
                    var values = new List<WaveformChunk>(batch.Count);
                    foreach (var (start, count, index) in batch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (count <= 0 || output == null)
                        {
                            if (index.HasValue)
                                values.Add(new WaveformChunk(index.Value, 0, 0));
                            continue;
                        }

                        using var buffer = Process(output, start, count);
                        if (index.HasValue)
                        {
                            for (int ch = 0; ch < buffer.ChannelCount; ch++)
                            {
                                var data = buffer.GetChannelData(ch);
                                AudioMath.ApplyLimiter(data);
                                AudioMath.ClampToFullScale(data);
                            }

                            var (min, max) = MeasureMonoRange(buffer, count);
                            values.Add(new WaveformChunk(index.Value, min, max));
                        }
                    }
                    return values;
                }, DispatchPriority.Low, cancellationToken).ConfigureAwait(false);

                foreach (var chunk in chunks)
                    yield return chunk;
            }

            AudioBuffer Process(AudioNode node, long start, int count)
            {
                var range = new TimeRange(MediaTime.FromSamples(start, sampleRate),
                    AudioProcessContext.GetDurationForSampleCount(count, sampleRate));
                return node.Process(new AudioProcessContext(
                    range, sampleRate, sampler, range.AddStart(clipRange.Start + startDelta)));
            }

            IEnumerable<List<(long Start, int Count, int? Index)>> BatchWindows()
            {
                var batch = new List<(long, int, int?)>(64);
                long samples = 0;
                foreach (var window in ProcessingWindows())
                {
                    batch.Add(window);
                    samples += window.Count;
                    if (samples >= sampleRate || batch.Count == 64)
                    {
                        yield return batch;
                        batch.Clear();
                        samples = 0;
                    }
                }
                if (batch.Count > 0)
                    yield return batch;
            }

            IEnumerable<(long Start, int Count, int? Index)> ProcessingWindows()
            {
                long? processedUntil = null;
                foreach (int index in indices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long firstSample = GetSamplePosition(index);
                    int count = (int)Math.Min(GetSamplePosition(index + 1) - firstSample, samplesPerChunk);
                    long start = firstSample + origin;
                    if (count <= 0 || output == null || finiteEndSample is { } end && start >= end)
                    {
                        yield return (start, 0, index);
                        continue;
                    }

                    if (historySamples != 0 && start > 0 && processedUntil != start)
                    {
                        foreach (var (from, length) in HistoryWindows(index, start, processedUntil))
                            yield return (from, length, null);
                    }

                    yield return (start, count, index);
                    processedUntil = start + count;
                }
            }

            IEnumerable<(long Start, int Count)> HistoryWindows(int index, long start, long? processedUntil)
            {
                long earliest = historySamples is { } span ? Math.Max(0, start - span) : 0;
                if (processedUntil is { } previousEnd && previousEnd > earliest && previousEnd < start)
                    earliest = previousEnd;

                // Start at the latest seek caused by the sample cap, or at the required history boundary.
                int first = index;
                long firstStart = start;
                while (firstStart > earliest)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long precedingStart = origin + GetSamplePosition(first - 1);
                    if (firstStart - precedingStart > samplesPerChunk)
                        break;

                    first--;
                    firstStart = precedingStart;
                }

                if (historySamples.HasValue)
                {
                    // A chain of feedback-free delays is sample-wise, so warm it in larger blocks.
                    long from = Math.Max(firstStart, earliest);
                    while (from < start)
                    {
                        int count = (int)Math.Min(start - from, samplesPerChunk);
                        yield return (from, count);
                        from += count;
                    }
                    yield break;
                }

                for (int slot = first; slot < index; slot++)
                {
                    long next = origin + GetSamplePosition(slot + 1);
                    long from = Math.Max(firstStart, earliest);
                    if (next > from)
                        yield return (from, (int)(next - from));
                    firstStart = next;
                }
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

    // A feedback-free delay has a finite history. Other effects may depend on every sample since a seek.
    private static long? GetPreviewEffectHistory(AudioEffect? effect, int sampleRate)
    {
        if (effect is not { IsEnabled.Value: true })
            return 0;

        if (effect is AudioEffectGroup group)
        {
            long result = 0;
            foreach (var child in group.Children)
            {
                if (GetPreviewEffectHistory(child, sampleRate) is not { } childHistory)
                    return null;
                result += childHistory;
            }

            return result;
        }

        if (effect is DelayEffect delay && !AnimationSampler.IsAnimated(delay.Feedback) && delay.Feedback.Value == 0)
        {
            float milliseconds = AnimationSampler.IsAnimated(delay.DelayTime)
                ? DelayParameters.DelayTimeMax
                : Math.Clamp(delay.DelayTime.Value, DelayParameters.DelayTimeMin, DelayParameters.DelayTimeMax);
            // The delay line reads before writing, so even a zero setting looks back one sample.
            return (long)Math.Ceiling(milliseconds / 1000d * sampleRate) + 1;
        }

        return null;
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
