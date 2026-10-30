using System.Runtime.CompilerServices;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
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
            OffsetPosition.Edited -= _thumbnailHandler;
            Speed.Edited -= _thumbnailHandler;
            IsLoop.Edited -= _thumbnailHandler;
        }
        _thumbnailHandler = null;
    }

    public string? GetThumbnailsCacheKey()
    {
        return ThumbnailsCacheKey.Compute(
            CoreSerializer.SerializeToJsonObject(this),
            ["Source", "OffsetPosition", "Speed", "IsLoop"]);
    }

    public IAsyncEnumerable<(int Index, int Count, Bitmap Thumbnail)> GetThumbnailStripAsync(
        int maxWidth,
        int maxHeight,
        IThumbnailCacheService? cacheService = null,
        CancellationToken cancellationToken = default,
        int startIndex = 0,
        int endIndex = -1)
        => GetThumbnailStripAsync(maxWidth, maxHeight, cacheService, cancellationToken, startIndex, endIndex,
            preferProxy: false,
            preferredProxyPreset: ProxyPreset.Quarter);

    // preferProxy false must keep the original decode path byte-identical (shared Default context).
    public async IAsyncEnumerable<(int Index, int Count, Bitmap Thumbnail)> GetThumbnailStripAsync(
        int maxWidth,
        int maxHeight,
        IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        int startIndex,
        int endIndex,
        bool preferProxy,
        ProxyPreset preferredProxyPreset = ProxyPreset.Quarter)
    {
        if (!await StripGate.TryEnterAsync(cancellationToken))
            yield break;

        Resource? resource = null;
        DrawableRenderNode? node = null;
        RenderNodeRenderer? renderer = null;
        try
        {
            // Creating the resource opens the media reader, which can block on metadata I/O.
            resource = await RenderThread.Dispatcher.InvokeAsync(() => ToResource(preferProxy
                ? new CompositionContext(MediaTime.Zero)
                {
                    PreferProxy = true,
                    PreferredProxyPreset = preferredProxyPreset,
                }
                : CompositionContext.Default), DispatchPriority.Medium, cancellationToken);

            if (resource.Source is not { } source)
                yield break;

            if (source.Duration <= MediaTime.Zero)
                yield break;
            var duration = TimeRange.Value.Duration;

            // Rendering draws the video into LogicalFrameSize; sizing the strip from the (possibly
            // smaller) proxy decode size would clip the frame. LogicalFrameSize equals FrameSize when
            // no proxy is active, so the non-proxy path stays byte-identical.
            var frameSize = source.LogicalFrameSize;
            int count = GetVideoThumbnailCount(frameSize, maxWidth, maxHeight);
            // A degenerate frame size (e.g. corrupt metadata with Height == 0) makes the division below
            // produce Infinity, which then overflows TimeSpan.FromSeconds. Bail out instead.
            if (count <= 0)
                yield break;
            // count > 0 implies frameSize dimensions and maxHeight are positive, so this is finite.
            float thumbWidth = maxHeight * (float)frameSize.Width / frameSize.Height;
            double interval = duration.ToSeconds() / count;

            string? baseKey = cacheService != null ? GetThumbnailsCacheKey() : null;
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

            for (int i = effectiveStart; i <= effectiveEnd; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                var time = MediaTime.FromSeconds(i * interval);

                if (cacheKey != null
                    && cacheService!.TryGet(cacheKey, time, cacheThreshold, out var cached)
                    && cached != null)
                {
                    yield return (i, count, cached);
                    continue;
                }

                var thumbnail = await RenderThread.Dispatcher.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return null;

                    var ctx = new CompositionContext(time + TimeRange.Value.Start)
                    {
                        PreferProxy = preferProxy,
                        PreferredProxyPreset = preferredProxyPreset,
                    };
                    bool versionBumped = false;
                    resource.Reconcile(this, ctx, ref versionBumped);

                    using (var gctx = new GraphicsContext2D(node, new Size((int)thumbWidth, maxHeight)))
                    using (gctx.PushTransform(Matrix.CreateScale(thumbWidth / frameSize.Width,
                               (float)maxHeight / frameSize.Height)))
                    {
                        DrawInternal(gctx, resource);
                    }

                    using RenderNodeRasterization rasterization = renderer.Rasterize();
                    return rasterization.Bitmap?.Clone();
                }, DispatchPriority.Medium, cancellationToken);

                if (thumbnail != null)
                {
                    if (cacheKey != null)
                        cacheService!.Save(cacheKey, time, thumbnail);

                    yield return (i, count, thumbnail);
                }
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
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
