using System.Runtime.CompilerServices;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.Serialization;
using SkiaSharp;

namespace Beutl.Graphics;

public partial class SourceVideo : IThumbnailsProvider
{
    // Bound decoder and CPU load when many clips enter the viewport together.
    internal static ThumbnailStripGate StripGate { get; } = new(2);

    private EventHandler? _thumbnailHandler;

    public ThumbnailsKind ThumbnailsKind => ThumbnailsKind.Video;

    public event EventHandler? ThumbnailsInvalidated;

    // Strip entries are stored under baseKey + suffix per preview-source mode; Invalidate(baseKey)
    // alone never hits them because nothing is stored under the unsuffixed key.
    public const string ProxyThumbnailCacheKeySuffix = "|proxy";
    public const string OriginalThumbnailCacheKeySuffix = "|original";

    public static void InvalidateThumbnailCacheKeys(IThumbnailCacheService cacheService, string? baseKey)
    {
        if (baseKey is null)
            return;
        foreach (ProxyPreset preset in Enum.GetValues<ProxyPreset>())
        {
            cacheService.Invalidate(baseKey + GetProxyThumbnailCacheKeySuffix(preset));
        }

        cacheService.Invalidate(baseKey + OriginalThumbnailCacheKeySuffix);
    }

    internal static string GetProxyThumbnailCacheKeySuffix(ProxyPreset preferredProxyPreset)
        => $"{ProxyThumbnailCacheKeySuffix}:{preferredProxyPreset}";

    // Edits to these change the frames that a strip shows.
    private IProperty[] GetThumbnailProperties()
        => [Source, TransferInterpretation, DeinterlaceMode, DeinterlaceFieldOrder, DeinterlaceMethod, DeinterlaceFrameRateMode,
            OffsetPosition, Speed, IsLoop];

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        _thumbnailHandler = (_, _) => ThumbnailsInvalidated?.Invoke(this, EventArgs.Empty);
        foreach (IProperty property in GetThumbnailProperties())
            property.Edited += _thumbnailHandler;
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (_thumbnailHandler != null)
        {
            foreach (IProperty property in GetThumbnailProperties())
                property.Edited -= _thumbnailHandler;
        }
        _thumbnailHandler = null;
    }

    public string? GetThumbnailsCacheKey()
    {
        string[] properties = [nameof(Source), nameof(OffsetPosition), nameof(Speed), nameof(IsLoop), nameof(TransferInterpretation)];
        if (GetDeinterlaceOptions() != VideoDeinterlaceOptions.Default)
        {
            properties = [.. properties, nameof(DeinterlaceMode), nameof(DeinterlaceFieldOrder), nameof(DeinterlaceMethod),
                nameof(DeinterlaceFrameRateMode)];
        }

        string key = ThumbnailsCacheKey.Compute(CoreSerializer.SerializeToJsonObject(this), properties);
        if (TransferInterpretation.Value != VideoTransferInterpretation.Auto)
            key += "|icc-transfer";

        return RequiresResampledRangePreview(startChanges: true)
            ? $"{key}|start:{TimeRange.Value.Start.Flicks}|duration:{TimeRange.Value.Duration.Flicks}"
            : key;
    }

    internal bool RequiresResampledRangePreview(bool startChanges)
    {
        bool? keyFrameGlobalClock = startChanges ? true : null;
        return AnimationSampler.RequiresPreviewResampling(Speed, keyFrameGlobalClock)
            || AnimationSampler.RequiresPreviewResampling(OffsetPosition, keyFrameGlobalClock)
            || AnimationSampler.RequiresPreviewResampling(Source, keyFrameGlobalClock)
            || AnimationSampler.RequiresPreviewResampling(IsLoop, keyFrameGlobalClock);
    }

    internal bool RequiresResampledSlipPreview
        => Speed.HasExpression || Speed.GetKeyFrameAnimation() is { KeyFrames.Count: > 0 };

    // A trim keeps the global clock at the same absolute times, while the local clock restarts at the new in-point.
    internal bool RequiresResampledTrimPreview
        => Speed.GetKeyFrameAnimation() is { KeyFrames.Count: > 0, UseGlobalClock.Value: false };

    public IAsyncEnumerable<(int Index, int Count, Bitmap Thumbnail)> GetThumbnailStripAsync(
        FontManager fonts,
        int maxWidth,
        int maxHeight,
        IThumbnailCacheService? cacheService = null,
        CancellationToken cancellationToken = default,
        int startIndex = 0,
        int endIndex = -1, DecoderRegistry? decoders = null)
        => GetThumbnailStripAsync(fonts, maxWidth, maxHeight, cacheService, cancellationToken, startIndex, endIndex,
            preferProxy: false,
            preferredProxyPreset: ProxyPreset.Quarter, decoders: decoders);

    public async IAsyncEnumerable<(int Index, int Count, Bitmap Thumbnail)> GetThumbnailStripAsync(
        FontManager fonts,
        int maxWidth,
        int maxHeight,
        IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        int startIndex,
        int endIndex,
        bool preferProxy,
        ProxyPreset preferredProxyPreset = ProxyPreset.Quarter, DecoderRegistry? decoders = null)
    {
        await foreach (var (index, count, thumbnail) in GetThumbnailStripCoreAsync(
                           fonts, maxWidth, maxHeight, cacheService, cancellationToken, startIndex, endIndex,
                           preferProxy, preferredProxyPreset, decoders))
        {
            using (thumbnail)
            {
                if (thumbnail != null)
                    yield return (index, count, thumbnail.Value.Clone());
            }
        }
    }

    internal async IAsyncEnumerable<(int Index, int Count, Bitmap? Thumbnail)> GetPreviewThumbnailStripAsync(
        FontManager fonts,
        int count,
        int maxHeight,
        IReadOnlyList<int> indices,
        IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        bool preferProxy,
        ProxyPreset preferredProxyPreset,
        DecoderRegistry? decoders,
        MediaTime offsetDelta,
        TimeRange previewLayout,
        MediaTime startDelta,
        MediaTime durationDelta)
    {
        await foreach (var (index, total, thumbnail) in GetPreviewThumbnailLeasesAsync(
                           fonts, count, maxHeight, indices, cacheService, cancellationToken, preferProxy,
                           preferredProxyPreset, decoders, offsetDelta, previewLayout, startDelta, durationDelta))
        {
            using (thumbnail)
                yield return (index, total, thumbnail?.Value.Clone());
        }
    }

    internal IAsyncEnumerable<(int Index, int Count, Ref<Bitmap>? Thumbnail)> GetPreviewThumbnailLeasesAsync(
        FontManager fonts, int count, int height, IReadOnlyList<int> indices, IThumbnailCacheService? cache,
        CancellationToken ct, bool preferProxy, ProxyPreset preset, DecoderRegistry? decoders,
        MediaTime offsetDelta, TimeRange previewLayout, MediaTime startDelta, MediaTime durationDelta)
        => GetThumbnailStripCoreAsync(fonts, 0, height, cache, ct, 0, -1, preferProxy, preset, decoders,
            selectIndices: _ => ValueTask.FromResult(indices), previewCount: count, offsetDelta: offsetDelta,
            previewLayout: previewLayout, startDelta: startDelta, durationDelta: durationDelta);

    internal IAsyncEnumerable<(int Index, int Count, Ref<Bitmap>? Thumbnail)> GetVisibleThumbnailLeasesAsync(
        FontManager fonts, int width, int height, IThumbnailCacheService cache, CancellationToken ct,
        bool preferProxy, ProxyPreset preset, DecoderRegistry? decoders,
        Func<int, ValueTask<IReadOnlyList<int>>> selectIndices)
        => GetThumbnailStripCoreAsync(fonts, width, height, cache, ct, 0, -1, preferProxy, preset, decoders, selectIndices);

    private async IAsyncEnumerable<(int Index, int Count, Ref<Bitmap>? Thumbnail)> GetThumbnailStripCoreAsync(
        FontManager fonts, int maxWidth, int maxHeight, IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken, int startIndex, int endIndex,
        bool preferProxy, ProxyPreset preferredProxyPreset, DecoderRegistry? decoders,
        Func<int, ValueTask<IReadOnlyList<int>>>? selectIndices = null, int previewCount = 0,
        MediaTime offsetDelta = default, TimeRange? previewLayout = null, MediaTime startDelta = default,
        MediaTime durationDelta = default)
    {
        if (maxHeight <= 0 || !await StripGate.TryEnterAsync(cancellationToken).ConfigureAwait(false))
            yield break;

        Resource? resource = null;
        try
        {
            int height = GetThumbnailHeightBucket(maxHeight);
            var conditions = CompositionConditions.Default with
            {
                Fonts = fonts,
                Decoders = decoders,
                PreferProxy = preferProxy,
                PreferredProxyPreset = preferredProxyPreset,
            };
            var duration = previewLayout?.Duration ?? TimeRange.Value.Duration;
            bool preview = previewLayout.HasValue;
            bool canUseKeyframes = !preview && selectIndices != null && !AnimationSampler.IsAnimated(Speed)
                && !AnimationSampler.IsAnimated(OffsetPosition) && !AnimationSampler.IsAnimated(Source)
                && !AnimationSampler.IsAnimated(IsLoop);

            CompositionContext ContextAt(MediaTime time) => CreateThumbnailContext(time, startDelta, conditions, durationDelta, height);

            resource = await Task.Run(() => ToResource(ContextAt(MediaTime.Zero)), cancellationToken).ConfigureAwait(false);

            // Slot geometry still uses the actual row height. Only the decoded bitmap is bucketed.
            var initialSize = resource.Source?.LogicalFrameSize ?? default;
            if (initialSize == default && AnimationSampler.IsAnimated(Source))
                initialSize = new PixelSize(16, 9);
            int count = preview ? previewCount : GetVideoThumbnailCount(initialSize, maxWidth, maxHeight);
            if (count <= 0)
                yield break;

            int first = Math.Max(0, startIndex);
            int last = endIndex < 0 ? count - 1 : Math.Min(endIndex, count - 1);
            IEnumerable<int> indices = selectIndices != null
                ? await selectIndices(count).ConfigureAwait(false)
                : Enumerable.Range(first, Math.Max(0, last - first + 1));
            double interval = duration.ToSeconds() / count;
            VideoSource.Resource? previousSource = null;
            int sourceVersion = -1;
            string? sourceKey = null;

            foreach (int[] batch in indices.Chunk(4))
            {
                var frames = await Task.Run(() =>
                {
                    var result = new Ref<Bitmap>?[batch.Length];
                    try
                    {
                        for (int j = 0; j < batch.Length; j++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            result[j] = ReadSlot(batch[j]);
                        }
                        return result;
                    }
                    catch
                    {
                        foreach (var frame in result) frame?.Dispose();
                        throw;
                    }
                }, cancellationToken).ConfigureAwait(false);

                try
                {
                    for (int j = 0; j < batch.Length; j++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var thumbnail = frames[j];
                        frames[j] = null;
                        if (thumbnail != null || selectIndices != null)
                            yield return (batch[j], count, thumbnail);
                    }
                }
                finally
                {
                    foreach (var thumbnail in frames) thumbnail?.Dispose();
                }
            }

            Ref<Bitmap>? ReadSlot(int index)
            {
                if (!preview && (index < 0 || index >= count))
                    return null;

                MediaTime time = MediaTime.FromSeconds(index * interval) + (previewLayout?.Start ?? MediaTime.Zero);
                bool versionBumped = false;
                resource.Reconcile(this, ContextAt(time < MediaTime.Zero ? MediaTime.Zero : time), ref versionBumped);
                if (resource.Source is not { IsDisposed: false } source)
                    return null;

                MediaTime position = resource.RequestedPosition + resource.OffsetPosition;
                if (preview && !TryComputePreviewSourcePosition(resource, time, out position, offsetDelta, startDelta))
                    return null;

                int frame = source.GetFrameIndex(position);
                if (cacheService != null && (previousSource != source || sourceVersion != source.Version))
                {
                    sourceKey = GetThumbnailSourceKey(source, decoders, preferProxy, preferredProxyPreset);
                    previousSource = source;
                    sourceVersion = source.Version;
                }

                var interpretation = resource.TransferInterpretation;
                string? exactKey = sourceKey is null ? null : $"{sourceKey}|{interpretation}|{height}";
                MediaTime frameTime = source.FrameRate.Numerator > 0 && source.FrameRate.Denominator > 0
                    ? MediaTime.FromFrames(frame, source.FrameRate)
                    : MediaTime.Zero;
                bool keyframe = canUseKeyframes && GetDeinterlaceOptions().Mode == Beutl.Media.Decoding.DeinterlaceMode.Off
                    && Math.Abs(interval * resource.Speed / 100d) >= 2;
                string? readKey = keyframe && exactKey != null ? exactKey + "|keyframe" : exactKey;
                if (exactKey != null)
                {
                    var hit = cacheService!.AcquireVideo(exactKey, frameTime)
                        ?? (keyframe ? cacheService.AcquireVideo(readKey!, frameTime) : null);
                    if (hit != null)
                        return hit;
                }

                Ref<Bitmap>? decoded = null;
                bool read = source.IsOffline
                    ? source.Read(frame, out decoded)
                    : source.MediaReader?.ReadVideoThumbnail(frame, keyframe, out decoded) == true;
                if (!read || decoded == null)
                    return null;

                using (decoded)
                {
                    var bitmap = Ref<Bitmap>.Create(RenderThumbnail(source, decoded.Value, interpretation, height));
                    try
                    {
                        if (readKey != null)
                            cacheService!.SaveVideo(readKey, frameTime, bitmap);
                        return bitmap;
                    }
                    catch
                    {
                        bitmap.Dispose();
                        throw;
                    }
                }
            }
        }
        finally
        {
            try
            {
                if (resource != null)
                    await Task.Run(resource.Dispose).ConfigureAwait(false);
            }
            finally
            {
                StripGate.Release();
            }
        }
    }

    private string? GetThumbnailSourceKey(VideoSource.Resource source, DecoderRegistry? decoders,
        bool preferProxy, ProxyPreset preferredProxyPreset)
    {
        if (decoders == null || source.IsOffline || source.GetOriginal() is not VideoSource { HasUri: true } original)
            return null;

        var proxy = source.ProxyResolution;
        if (!ProxyFingerprint.TryFromFile(original.Uri.LocalPath, out var fingerprint))
        {
            if (proxy == null)
                return null;
            fingerprint = proxy.Source;
        }

        string proxyIdentity = "";
        if (proxy != null)
        {
            if (!ProxyFingerprint.TryFromFile(proxy.AbsoluteProxyFilePath, out var proxyFile))
                return null;
            proxyIdentity = $"|{proxyFile.AbsolutePath}|{proxyFile.FileSizeBytes}|{proxyFile.MtimeUtc.Ticks}";
        }

        string mode = preferProxy ? GetProxyThumbnailCacheKeySuffix(preferredProxyPreset) : OriginalThumbnailCacheKeySuffix;
        return FormattableString.Invariant(
            $"video|{decoders.CacheId}|{decoders.Version}|{fingerprint.AbsolutePath}|{fingerprint.FileSizeBytes}|{fingerprint.MtimeUtc.Ticks}|{GetDeinterlaceOptions()}{mode}|{source.ThumbnailProxyVersion}{proxyIdentity}");
    }

    internal static int GetThumbnailHeightBucket(int height)
        => ((Math.Clamp(height, 1, 512) - 1) / 16 + 1) * 16;

    private static Bitmap RenderThumbnail(VideoSource.Resource source, Bitmap decoded,
        VideoTransferInterpretation interpretation, int height)
    {
        PixelSize size = source.LogicalFrameSize;
        int width = Math.Max(1, (int)Math.Round(height * (double)size.Width / size.Height));
        using Bitmap? recovered = ImmediateCanvas.RecoverVideoCodeValues(source, decoded, interpretation, out var colorOverride);
        using var image = (recovered ?? decoded).CreateImageWithoutCopy(colorOverride);
        var bitmap = new Bitmap(width, height, BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
        try
        {
            using var canvas = new SKCanvas(bitmap.SKBitmap);
            canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    internal CompositionContext CreateThumbnailContext(
        MediaTime relativeTime, MediaTime startDelta, CompositionConditions conditions, MediaTime durationDelta = default, int thumbnailHeight = 0)
    {
        TimeRange range = TimeRange.Value;
        MediaTime start = range.Start + startDelta;
        return new CompositionContext(start + relativeTime)
        {
            Conditions = conditions,
            IsThumbnail = thumbnailHeight > 0,
            VideoThumbnailHeight = thumbnailHeight,
            PreviewTimeRange = startDelta != MediaTime.Zero || durationDelta != MediaTime.Zero
                ? (this, start, range.Duration + durationDelta)
                : null
        };
    }

    internal bool TryComputePreviewSourcePosition(
        Resource resource,
        MediaTime relativeTime,
        out MediaTime sourcePosition,
        MediaTime offsetDelta = default,
        MediaTime startDelta = default)
    {
        sourcePosition = MediaTime.Zero;
        if (resource.Source is not { IsDisposed: false } source || source.Duration <= MediaTime.Zero)
            return false;

        MediaTime start = TimeRange.Value.Start + startDelta;
        MediaTime requested;
        if (relativeTime < MediaTime.Zero && Speed.GetKeyFrameAnimation() is KeyFrameAnimation<float> { KeyFrames.Count: > 0 } animation)
        {
            // Extrapolate before the clip using its starting rate.
            float startSpeed = animation.Interpolate(animation.UseGlobalClock.Value ? start : MediaTime.Zero);
            requested = new MediaTime((long)Math.Round(relativeTime.Flicks * (double)(startSpeed / 100f)));
        }
        else
        {
            requested = CalculateRequestedPosition(start + relativeTime, resource, start);
        }

        // Playback can move backwards after the in-point. Extending before it can also expose empty media space.
        MediaTime offset = resource.OffsetPosition + offsetDelta;
        MediaTime position = NormalizeRequestedPosition(resource, requested, offset, normalizeReverse: relativeTime >= MediaTime.Zero) + offset;

        if (position < MediaTime.Zero || position >= source.Duration)
            return false;

        sourcePosition = position;
        return true;
    }

    public async IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(
        int chunkCount,
        int samplesPerChunk,
        IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken = default, DecoderRegistry? decoders = null)
    {
        await Task.CompletedTask;
        yield break;
    }

    internal static int GetVideoThumbnailCount(PixelSize frameSize, int maxWidth, int maxHeight)
    {
        if (frameSize.Width <= 0 || frameSize.Height <= 0 || maxWidth <= 0 || maxHeight <= 0)
            return 0;

        float aspectRatio = (float)frameSize.Width / frameSize.Height;
        float thumbWidth = maxHeight * aspectRatio;
        if (!float.IsFinite(thumbWidth) || thumbWidth <= 0)
            return 0;

        float raw = MathF.Ceiling(maxWidth / thumbWidth);
        if (!float.IsFinite(raw) || raw <= 0 || raw > int.MaxValue)
            return 0;

        return (int)raw;
    }
}
