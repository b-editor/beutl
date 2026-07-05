using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Threading;

namespace Beutl.Graphics;

public partial class SourceVideo : IThumbnailsProvider
{
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
        cacheService.Invalidate(baseKey + ProxyThumbnailCacheKeySuffix);
        cacheService.Invalidate(baseKey + OriginalThumbnailCacheKeySuffix);
    }

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
        var fullJson = CoreSerializer.SerializeToJsonObject(this);
        var cacheJson = new JsonObject();
        string[] targetProps = ["Source", "OffsetPosition", "Speed", "IsLoop"];

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

    public IAsyncEnumerable<(int Index, int Count, Bitmap Thumbnail)> GetThumbnailStripAsync(
        int maxWidth,
        int maxHeight,
        IThumbnailCacheService? cacheService = null,
        CancellationToken cancellationToken = default,
        int startIndex = 0,
        int endIndex = -1)
        => GetThumbnailStripAsync(maxWidth, maxHeight, cacheService, cancellationToken, startIndex, endIndex,
            preferProxy: false);

    // preferProxy false must keep the original decode path byte-identical (shared Default context).
    public async IAsyncEnumerable<(int Index, int Count, Bitmap Thumbnail)> GetThumbnailStripAsync(
        int maxWidth,
        int maxHeight,
        IThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        int startIndex,
        int endIndex,
        bool preferProxy)
    {
        Resource? resource = null;
        DrawableRenderNode? node = null;
        try
        {
            resource = ToResource(preferProxy
                ? new CompositionContext(TimeSpan.Zero) { PreferProxy = true }
                : CompositionContext.Default);

            if (resource.Source is not { } source)
                yield break;

            if (source.Duration <= TimeSpan.Zero)
                yield break;
            var duration = TimeRange.Duration;

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
            double interval = duration.TotalSeconds / count;

            string? baseKey = cacheService != null ? GetThumbnailsCacheKey() : null;
            // Partition by preview-source mode: ThumbnailsInvalidated covers mode changes for one
            // view, but two same-source views in different modes (or a fresh view after a prior proxy
            // render) would otherwise collide on the mode-agnostic base key.
            string? cacheKey = baseKey != null
                ? baseKey + (preferProxy ? ProxyThumbnailCacheKeySuffix : OriginalThumbnailCacheKeySuffix)
                : null;
            var cacheThreshold = TimeSpan.FromSeconds(interval * 0.5);

            int effectiveStart = Math.Max(0, startIndex);
            int effectiveEnd = endIndex < 0 ? count - 1 : Math.Min(endIndex, count - 1);

            node = new DrawableRenderNode(resource);
            var processor = new RenderNodeProcessor(node, false);

            for (int i = effectiveStart; i <= effectiveEnd; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                var time = TimeSpan.FromSeconds(i * interval);

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

                    var ctx = new CompositionContext(time + TimeRange.Start) { PreferProxy = preferProxy };
                    bool updateOnly = false;
                    resource.Update(this, ctx, ref updateOnly);

                    using (var gctx = new GraphicsContext2D(node, new Size((int)thumbWidth, maxHeight)))
                    using (gctx.PushTransform(Matrix.CreateScale(thumbWidth / frameSize.Width,
                               (float)maxHeight / frameSize.Height)))
                    {
                        DrawInternal(gctx, resource);
                    }

                    return processor.RasterizeAndConcat();
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
            RenderThread.Dispatcher.Dispatch(() =>
            {
                node?.Dispose();
                resource?.Dispose();
            }, ct: CancellationToken.None);
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
