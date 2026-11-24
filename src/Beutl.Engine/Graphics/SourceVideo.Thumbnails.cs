using System.Runtime.CompilerServices;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Serialization;
using Beutl.Threading;

namespace Beutl.Graphics;

public partial class SourceVideo : IThumbnailsProvider
{
    // A strip opens its own decoder unless the preview already holds one for the source, and every
    // element of a timeline requests its strip at once (e.g. on project load). The strips render on the
    // single render thread anyway, so running more of them concurrently only multiplies open decoders.
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

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        _thumbnailHandler = (_, _) => ThumbnailsInvalidated?.Invoke(this, EventArgs.Empty);
        Source.Edited += _thumbnailHandler;
        TransferInterpretation.Edited += _thumbnailHandler;
        DeinterlaceMode.Edited += _thumbnailHandler;
        DeinterlaceFieldOrder.Edited += _thumbnailHandler;
        DeinterlaceMethod.Edited += _thumbnailHandler;
        DeinterlaceFrameRateMode.Edited += _thumbnailHandler;
        OffsetPosition.Edited += _thumbnailHandler;
        Speed.Edited += _thumbnailHandler;
        IsLoop.Edited += _thumbnailHandler;
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (_thumbnailHandler != null)
        {
            Source.Edited -= _thumbnailHandler;
            TransferInterpretation.Edited -= _thumbnailHandler;
            DeinterlaceMode.Edited -= _thumbnailHandler;
            DeinterlaceFieldOrder.Edited -= _thumbnailHandler;
            DeinterlaceMethod.Edited -= _thumbnailHandler;
            DeinterlaceFrameRateMode.Edited -= _thumbnailHandler;
            OffsetPosition.Edited -= _thumbnailHandler;
            Speed.Edited -= _thumbnailHandler;
            IsLoop.Edited -= _thumbnailHandler;
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

        string? key = ThumbnailsCacheKey.Compute(CoreSerializer.SerializeToJsonObject(this), properties);
        return key != null && RequiresResampledStartPreview ? $"{key}|start:{TimeRange.Value.Start.Flicks}" : key;
    }

    internal bool RequiresResampledStartPreview
        => AnimationSampler.DependsOnTimelineStart(Speed)
            || AnimationSampler.DependsOnTimelineStart(OffsetPosition)
            || AnimationSampler.DependsOnTimelineStart(Source)
            || AnimationSampler.DependsOnTimelineStart(IsLoop);

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
            if (thumbnail != null)
                yield return (index, count, thumbnail);
        }
    }

    internal IAsyncEnumerable<(int Index, int Count, Bitmap? Thumbnail)> GetPreviewThumbnailStripAsync(
        FontManager fonts,
        int count,
        int maxHeight,
        IReadOnlyList<int> indices,
        IThumbnailCacheService? cacheService,
        CancellationToken cancellationToken,
        bool preferProxy,
        ProxyPreset preferredProxyPreset,
        DecoderRegistry? decoders,
        MediaTime offsetDelta,
        TimeRange previewLayout,
        MediaTime startDelta)
        => GetThumbnailStripCoreAsync(fonts, 0, maxHeight, cacheService, cancellationToken, 0, -1,
            preferProxy, preferredProxyPreset, decoders, indices, count, offsetDelta, previewLayout, startDelta);

    private async IAsyncEnumerable<(int Index, int Count, Bitmap? Thumbnail)> GetThumbnailStripCoreAsync(
        FontManager fonts,
        int maxWidth,
        int maxHeight,
        IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        int startIndex,
        int endIndex,
        bool preferProxy,
        ProxyPreset preferredProxyPreset,
        DecoderRegistry? decoders,
        IReadOnlyList<int>? previewIndices = null,
        int previewCount = 0,
        MediaTime offsetDelta = default,
        TimeRange? previewLayout = null,
        MediaTime startDelta = default)
    {
        if (previewIndices is { Count: 0 })
            yield break;

        if (!await StripGate.TryEnterAsync(cancellationToken))
            yield break;

        Resource? resource = null;
        DrawableRenderNode? node = null;
        RenderNodeRenderer? renderer = null;
        try
        {
            var conditions = CompositionConditions.Default with
            {
                Fonts = fonts,
                Decoders = decoders,
                PreferProxy = preferProxy,
                PreferredProxyPreset = preferredProxyPreset,
            };
            // Opening the decoder can parse a large media file. Queue it with the strip's rendering work.
            resource = await RenderThread.Dispatcher.InvokeAsync(() => ToResource(previewIndices != null
                ? CreateThumbnailContext(MediaTime.Zero, startDelta, conditions)
                : new CompositionContext(MediaTime.Zero) { Conditions = conditions }), DispatchPriority.Low, cancellationToken);

            if (resource.Source is not { } source)
                yield break;

            if (source.Duration <= MediaTime.Zero)
                yield break;
            var duration = previewLayout?.Duration ?? TimeRange.Value.Duration;

            // Rendering draws the video into LogicalFrameSize; sizing the strip from the (possibly
            // smaller) proxy decode size would clip the frame. LogicalFrameSize equals FrameSize when
            // no proxy is active, so the non-proxy path stays byte-identical.
            var frameSize = source.LogicalFrameSize;
            int count = previewIndices != null ? previewCount : GetVideoThumbnailCount(frameSize, maxWidth, maxHeight);
            // A degenerate frame size (e.g. corrupt metadata with Height == 0) makes the division below
            // produce Infinity, which then overflows TimeSpan.FromSeconds. Bail out instead.
            if (count <= 0 || frameSize.Width <= 0 || frameSize.Height <= 0 || maxHeight <= 0)
                yield break;
            float thumbWidth = maxHeight * (float)frameSize.Width / frameSize.Height;
            double interval = duration.ToSeconds() / count;

            string? baseKey = offsetDelta == MediaTime.Zero && startDelta == MediaTime.Zero && cacheService != null
                ? GetThumbnailsCacheKey()
                : null;
            // Partition by preview-source mode: ThumbnailsInvalidated covers mode changes for one
            // view, but two same-source views in different modes (or a fresh view after a prior proxy
            // render) would otherwise collide on the mode-agnostic base key.
            string? cacheKey = baseKey != null
                ? baseKey + (preferProxy ? GetProxyThumbnailCacheKeySuffix(preferredProxyPreset) : OriginalThumbnailCacheKeySuffix)
                : null;
            var cacheThreshold = MediaTime.FromSeconds(interval * 0.5);

            int effectiveStart = Math.Max(0, startIndex);
            int effectiveEnd = endIndex < 0 ? count - 1 : Math.Min(endIndex, count - 1);

            node = new DrawableRenderNode(resource);
            renderer = new RenderNodeRenderer(
                node,
                new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = new Rect(0, 0, thumbWidth, maxHeight),
                    OutputScale = 1,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                });

            IEnumerable<int> indices = (IEnumerable<int>?)previewIndices ?? Enumerable.Range(
                effectiveStart, Math.Max(0, effectiveEnd - effectiveStart + 1));
            foreach (int i in indices)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                var time = MediaTime.FromSeconds(i * interval) + (previewLayout?.Start ?? MediaTime.Zero);

                if (cacheKey != null
                    && cacheService!.TryGet(cacheKey, time, cacheThreshold, out var cached)
                    && cached != null)
                {
                    if (cached.Height == maxHeight)
                    {
                        yield return (i, count, cached);
                        continue;
                    }

                    cached.Dispose();
                }

                var (hasFrame, thumbnail) = await RenderThread.Dispatcher.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return (false, (Bitmap?)null);

                    MediaTime relativeEvalTime = previewIndices != null && time < MediaTime.Zero ? MediaTime.Zero : time;
                    var ctx = CreateThumbnailContext(relativeEvalTime, startDelta, conditions);
                    bool versionBumped = false;
                    resource.Reconcile(this, ctx, ref versionBumped);

                    MediaTime? previewPosition = null;
                    if (previewIndices != null)
                    {
                        if (!TryComputePreviewSourcePosition(resource, time, out MediaTime sourcePosition, offsetDelta, startDelta))
                            return (false, (Bitmap?)null);

                        previewPosition = sourcePosition;
                    }

                    using (var gctx = new GraphicsContext2D(node, new Size((int)thumbWidth, maxHeight)))
                    using (gctx.PushTransform(Matrix.CreateScale(thumbWidth / frameSize.Width,
                               (float)maxHeight / frameSize.Height)))
                    {
                        resource.DrawInternal(gctx, previewPosition);
                    }

                    using RenderNodeRasterization rasterization = renderer.Rasterize();
                    return (true, rasterization.Bitmap?.Clone());
                }, DispatchPriority.Low, cancellationToken);

                if (thumbnail != null && cacheKey != null)
                    cacheService!.Save(cacheKey, time, thumbnail);

                // A null preview result marks a slot outside the source as resolved.
                if (thumbnail != null || !hasFrame && previewIndices != null)
                    yield return (i, count, thumbnail);
            }
        }
        finally
        {
            try
            {
                await DisposeThumbnailRenderResourcesAsync(renderer, node, resource);
            }
            finally
            {
                StripGate.Release();
            }
        }
    }

    internal CompositionContext CreateThumbnailContext(MediaTime relativeTime, MediaTime startDelta, CompositionConditions conditions)
    {
        TimeRange range = TimeRange.Value;
        MediaTime start = range.Start + startDelta;
        return new CompositionContext(start + relativeTime)
        {
            Conditions = conditions,
            PreviewTimeRange = startDelta != MediaTime.Zero ? (this, start, range.Duration - startDelta) : null
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

    internal static Task DisposeThumbnailRenderResourcesAsync(params IDisposable?[] resources)
        => DisposeThumbnailRenderResourcesAsync(RenderThread.Dispatcher, resources);

    internal static async Task DisposeThumbnailRenderResourcesAsync(
        Dispatcher dispatcher,
        params IDisposable?[] resources)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(resources);
        if (dispatcher.HasShutdownStarted)
            return;

        // Not disposed, so a shutdown handler copied before the unsubscribe below can still cancel it.
        var shutdownCancellation = new CancellationTokenSource();
        void OnShutdownStarted(object? _, EventArgs __) => shutdownCancellation.Cancel();
        dispatcher.ShutdownStarted += OnShutdownStarted;
        try
        {
            if (dispatcher.HasShutdownStarted)
                return;

            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    Exception? primary = null;
                    foreach (IDisposable? resource in resources)
                    {
                        try
                        {
                            resource?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            primary ??= ex;
                        }
                    }

                    if (primary is not null)
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
                }, ct: shutdownCancellation.Token);
            }
            catch (OperationCanceledException) when (dispatcher.HasShutdownStarted)
            {
                // RenderThread shutdown occurs only at process exit. Queued cleanup cannot run, and these
                // thread-affine resources cannot be disposed safely from the caller thread.
            }
        }
        finally
        {
            dispatcher.ShutdownStarted -= OnShutdownStarted;
        }
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
