using System.Runtime.CompilerServices;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class SourceImageOperator : PublishOperator<SourceImage>, IElementPreviewProvider
{
    private Uri? _uri;

    public ElementPreviewKind PreviewKind => ElementPreviewKind.Image;

    protected override void FillProperties()
    {
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

        if (BitmapSource.TryOpen(_uri, out BitmapSource? imageSource))
        {
            value.Source.CurrentValue = imageSource;
        }
    }

    public Task<IBitmap?> GetPreviewBitmapAsync(int maxWidth, int maxHeight, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            if (Value?.Source.CurrentValue is not { IsDisposed: false } source)
                return null;

            if (!source.Read(out IBitmap? bitmap))
                return null;

            if (bitmap.Width > maxWidth || bitmap.Height > maxHeight)
            {
                float scale = Math.Min((float)maxWidth / bitmap.Width, (float)maxHeight / bitmap.Height);
                int newWidth = (int)(bitmap.Width * scale);
                int newHeight = (int)(bitmap.Height * scale);

                using var original = bitmap;
                return SourceVideoOperator.ScaleBitmap(original, newWidth, newHeight);
            }

            return bitmap;
        }, cancellationToken);
    }

    public async IAsyncEnumerable<(int Index, IBitmap Thumbnail)> GetThumbnailStripAsync(
        int count,
        int maxHeight,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
