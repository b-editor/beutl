using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Operation;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.Video), ResourceType = typeof(Strings))]
public sealed class SourceVideoOperator : PublishOperator<SourceVideo>, IElementThumbnailsProvider
{
    private EventHandler? _handler;

    public ElementThumbnailsKind ThumbnailsKind => ElementThumbnailsKind.Video;

    public event EventHandler? ThumbnailsInvalidated;

    protected override void FillProperties()
    {
        AddProperty(Value.OffsetPosition, TimeSpan.Zero);
        AddProperty(Value.Speed, 100f);
        AddProperty(Value.Source);
        AddProperty(Value.IsLoop);
        AddProperty(Value.Transform, new TransformGroup());
        AddProperty(Value.AlignmentX);
        AddProperty(Value.AlignmentY);
        AddProperty(Value.TransformOrigin);
        AddProperty(Value.FilterEffect, new FilterEffectGroup());
        AddProperty(Value.BlendMode);
        AddProperty(Value.Opacity);
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);

        if (Value is { } value && _handler != null)
        {
            value.Source.Edited -= _handler;
            value.OffsetPosition.Edited -= _handler;
            value.Speed.Edited -= _handler;
            value.IsLoop.Edited -= _handler;
        }

        _handler = null;
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (Value is not { } value) return;

        _handler = (_, _) => ThumbnailsInvalidated?.Invoke(this, EventArgs.Empty);

        value.Source.Edited += _handler;
        value.OffsetPosition.Edited += _handler;
        value.Speed.Edited += _handler;
        value.IsLoop.Edited += _handler;
    }

    public override bool HasOriginalLength()
    {
        return Value?.Source.CurrentValue != null;
    }

    public override bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        using var resource = Value.ToResource(RenderContext.Default);
        var ts = Value.CalculateOriginalTime(resource);
        if (ts.HasValue)
        {
            timeSpan = ts.Value - Value.OffsetPosition.CurrentValue;
            return true;
        }
        else
        {
            timeSpan = TimeSpan.Zero;
            return false;
        }
    }

    public override void OnSplit(bool backward, TimeSpan startDelta, TimeSpan lengthDelta)
    {
        if (Value is null) return;

        if (backward)
        {
            Value.OffsetPosition.CurrentValue += startDelta;
        }
        else
        {
            base.OnSplit(backward, startDelta, lengthDelta);
        }
    }

    public async IAsyncEnumerable<(int Index, int Count, IBitmap Thumbnail)> GetThumbnailStripAsync(
        int maxWidth,
        int maxHeight,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var resource = Value.ToResource(RenderContext.Default);

        if (resource.Source is not { } source)
            yield break;

        if (resource.Source.Duration <= TimeSpan.Zero)
            yield break;
        var duration = Value.TimeRange.Duration;

        var frameSize = source.FrameSize;
        float aspectRatio = (float)frameSize.Width / frameSize.Height;
        float thumbWidth = maxHeight * aspectRatio;
        int count = (int)MathF.Ceiling(maxWidth / thumbWidth);
        double interval = duration.TotalSeconds / count;
        var node = new DrawableRenderNode(resource);
        var processor = new RenderNodeProcessor(node, false);

        try
        {
            for (int i = 0; i < count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                var time = TimeSpan.FromSeconds(i * interval);

                var thumbnail = await RenderThread.Dispatcher.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return null;

                    var ctx = new RenderContext(time + Value.TimeRange.Start);
                    bool updateOnly = false;
                    resource.Update(Value, ctx, ref updateOnly);

                    using (var gctx = new GraphicsContext2D(node, new PixelSize((int)thumbWidth, maxHeight)))
                    using (gctx.PushTransform(Matrix.CreateScale(thumbWidth / frameSize.Width,
                               (float)maxHeight / frameSize.Height)))
                    {
                        Value.DrawInternal(gctx, resource);
                    }

                    return processor.RasterizeAndConcat();
                }, DispatchPriority.Medium, cancellationToken);

                if (thumbnail != null)
                {
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
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
