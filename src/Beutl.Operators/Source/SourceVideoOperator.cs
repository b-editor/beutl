using System.Runtime.CompilerServices;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Operation;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Operators.Source;

public sealed class SourceVideoOperator : PublishOperator<SourceVideo>, IElementPreviewProvider
{
    private Uri? _uri;
    private EventHandler? _handler;

    public ElementPreviewKind PreviewKind => ElementPreviewKind.Video;

    public event EventHandler? PreviewInvalidated;

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

        if (Value is { } value)
        {
            if (_handler != null)
            {
                value.Source.Edited -= _handler;
                value.OffsetPosition.Edited -= _handler;
                value.Speed.Edited -= _handler;
                value.IsLoop.Edited -= _handler;
            }
        }

        _handler = null;

        if (Value is not { Source.CurrentValue: { Uri: { } uri } source } v) return;

        _uri = uri;
        v.Source.CurrentValue = null;
        source.Dispose();
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (Value is not { } value) return;

        _handler = (_, _) => PreviewInvalidated?.Invoke(this, EventArgs.Empty);

        value.Source.Edited += _handler;
        value.OffsetPosition.Edited += _handler;
        value.Speed.Edited += _handler;
        value.IsLoop.Edited += _handler;

        if (_uri is null) return;

        if (VideoSource.TryOpen(_uri, out VideoSource? source))
        {
            value.Source.CurrentValue = source;
        }
    }

    public override bool HasOriginalLength()
    {
        return Value?.Source.CurrentValue?.IsDisposed == false;
    }

    public override bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        var ts = Value.CalculateOriginalTime();
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

    public override IRecordableCommand? OnSplit(bool backward, TimeSpan startDelta, TimeSpan lengthDelta)
    {
        if (Value is null) return null;

        if (backward)
        {
            TimeSpan newValue = Value.OffsetPosition.CurrentValue + startDelta;
            TimeSpan oldValue = Value.OffsetPosition.CurrentValue;

            return RecordableCommands.Create([this])
                .OnDo(() => Value.OffsetPosition.CurrentValue = newValue)
                .OnUndo(() => Value.OffsetPosition.CurrentValue = oldValue)
                .ToCommand();
        }
        else
        {
            return base.OnSplit(backward, startDelta, lengthDelta);
        }
    }

    public async IAsyncEnumerable<(int Index, IBitmap Thumbnail)> GetThumbnailStripAsync(
        int count,
        int maxHeight,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Value.Source.CurrentValue is not { IsDisposed: false } source)
            yield break;

        if (count <= 0)
            yield break;

        if (source.Duration <= TimeSpan.Zero)
            yield break;
        var duration = Value.TimeRange.Duration;

        var interval = duration.TotalSeconds / count;

        var frameSize = source.FrameSize;
        float aspectRatio = (float)frameSize.Width / frameSize.Height;
        int thumbWidth = (int)(maxHeight * aspectRatio);
        SourceVideo.Resource? resource = null;
        DrawableRenderNode? node = null;
        RenderNodeProcessor? processor = null;

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
                    if (resource == null)
                    {
                        resource = Value.ToResource(ctx);
                        node = new DrawableRenderNode(resource);
                        processor = new RenderNodeProcessor(node, false);
                    }
                    else
                    {
                        bool updateOnly = false;
                        resource.Update(Value, ctx, ref updateOnly);
                    }

                    using (var gctx = new GraphicsContext2D(node!, new PixelSize(thumbWidth, maxHeight)))
                    using (gctx.PushTransform(Matrix.CreateScale((float)thumbWidth / frameSize.Width, (float)maxHeight / frameSize.Height)))
                    {
                        Value.DrawInternal(gctx, resource);
                    }

                    return processor!.RasterizeAndConcat();
                }, DispatchPriority.Medium, cancellationToken);

                if (thumbnail != null)
                {
                    yield return (i, thumbnail);
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
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
