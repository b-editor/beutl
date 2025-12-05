using System.Runtime.CompilerServices;
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

    public ElementPreviewKind PreviewKind => ElementPreviewKind.Video;

    protected override void FillProperties()
    {
        AddProperty(Value.OffsetPosition, TimeSpan.Zero);
        AddProperty(Value.Source);
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
        if (Value is not { Source.CurrentValue: { Uri: { } uri } source } value) return;

        _uri = uri;
        value.Source.CurrentValue = null;
        source.Dispose();
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (_uri is null) return;
        if (Value is not { } value) return;

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

    public Task<IBitmap?> GetPreviewBitmapAsync(int maxWidth, int maxHeight, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IBitmap?>(null);
    }

    public async IAsyncEnumerable<(int Index, IBitmap Thumbnail)> GetThumbnailStripAsync(
        int count,
        int maxHeight,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Value?.Source.CurrentValue is not { IsDisposed: false } source)
            yield break;

        if (count <= 0)
            yield break;

        var duration = source.Duration;
        if (duration <= TimeSpan.Zero)
            yield break;

        var interval = duration.TotalSeconds / count;

        var frameSize = source.FrameSize;
        float aspectRatio = (float)frameSize.Width / frameSize.Height;
        int thumbWidth = (int)(maxHeight * aspectRatio);

        for (int i = 0; i < count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var time = TimeSpan.FromSeconds(i * interval);

            var thumbnail = await RenderThread.Dispatcher.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return null;

                if (!source.Read(time, out IBitmap? frame))
                    return null;

                if (frame.Height != maxHeight)
                {
                    using var original = frame;
                    return ScaleBitmap(original, thumbWidth, maxHeight);
                }

                return frame;
            }, DispatchPriority.Medium, cancellationToken);

            if (thumbnail != null)
            {
                yield return (i, thumbnail);
            }
        }
    }

    public static IBitmap? ScaleBitmap(IBitmap source, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return null;

        using var scaledImage = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(scaledImage);
        using var bitmap = source.ToSKBitmap();

        canvas.DrawBitmap(
            bitmap,
            new SKRect(0, 0, width, height));

        return scaledImage.ToBitmap();
    }
}
