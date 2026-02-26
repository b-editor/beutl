using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Threading;

namespace Beutl.Graphics;

public partial class SourceVideo : IElementThumbnailsProvider
{
    private EventHandler? _thumbnailHandler;

    public ElementThumbnailsKind ThumbnailsKind => ElementThumbnailsKind.Video;

    public event EventHandler? ThumbnailsInvalidated;

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

    public async IAsyncEnumerable<(int Index, int Count, IBitmap Thumbnail)> GetThumbnailStripAsync(
        int maxWidth,
        int maxHeight,
        IElementThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        int startIndex = 0,
        int endIndex = -1)
    {
        using var resource = ToResource(RenderContext.Default);

        if (((Resource)resource).Source is not { } source)
            yield break;

        if (source.Duration <= TimeSpan.Zero)
            yield break;
        var duration = TimeRange.Duration;

        var frameSize = source.FrameSize;
        float aspectRatio = (float)frameSize.Width / frameSize.Height;
        float thumbWidth = maxHeight * aspectRatio;
        int count = (int)MathF.Ceiling(maxWidth / thumbWidth);
        double interval = duration.TotalSeconds / count;

        string? cacheKey = cacheService != null ? GetThumbnailsCacheKey() : null;
        var cacheThreshold = TimeSpan.FromSeconds(interval * 0.5);

        int effectiveStart = Math.Max(0, startIndex);
        int effectiveEnd = endIndex < 0 ? count - 1 : Math.Min(endIndex, count - 1);

        var node = new DrawableRenderNode(resource);
        var processor = new RenderNodeProcessor(node, false);

        try
        {
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

                    var ctx = new RenderContext(time + TimeRange.Start);
                    bool updateOnly = false;
                    resource.Update(this, ctx, ref updateOnly);

                    using (var gctx = new GraphicsContext2D(node, new PixelSize((int)thumbWidth, maxHeight)))
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
                node.Dispose();
            }, ct: CancellationToken.None);
        }
    }

    public async IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(
        int chunkCount,
        int samplesPerChunk,
        IElementThumbnailCacheService? cacheService,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
