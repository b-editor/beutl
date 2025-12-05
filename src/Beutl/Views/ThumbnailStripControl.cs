using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Stretch = Avalonia.Media.Stretch;

namespace Beutl.Views;

public sealed class ThumbnailStripControl : Control
{
    public static readonly StyledProperty<int> ThumbnailCountProperty =
        AvaloniaProperty.Register<ThumbnailStripControl, int>(nameof(ThumbnailCount));

    private readonly Dictionary<int, Bitmap?> _thumbnails = new();
    private readonly Lock _lock = new();

    static ThumbnailStripControl()
    {
        AffectsRender<ThumbnailStripControl>(ThumbnailCountProperty);
    }

    public int ThumbnailCount
    {
        get => GetValue(ThumbnailCountProperty);
        set => SetValue(ThumbnailCountProperty, value);
    }

    public void SetThumbnail(int index, Bitmap? thumbnail)
    {
        lock (_lock)
        {
            if (_thumbnails.TryGetValue(index, out var old))
            {
                old?.Dispose();
            }

            _thumbnails[index] = thumbnail;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
    }

    public void ClearThumbnails()
    {
        lock (_lock)
        {
            foreach (var kvp in _thumbnails)
            {
                kvp.Value?.Dispose();
            }

            _thumbnails.Clear();
        }

        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ThumbnailCountProperty)
        {
            ClearThumbnails();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        double width = bounds.Width;
        double height = bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        int count = ThumbnailCount;
        if (count <= 0)
            return;

        double slotWidth = width / count;

        lock (_lock)
        {
            foreach (var kvp in _thumbnails)
            {
                int i = kvp.Key;
                var img = kvp.Value;

                if (img == null || i < 0 || i >= count)
                    continue;

                double slotX = i * slotWidth;

                var dstSize = new Size(slotWidth, height);
                var srcSize = img.Size;
                var viewPort = new Rect(dstSize);

                Vector scale = Stretch.UniformToFill.CalculateScaling(dstSize, srcSize);
                Size scaledSize = srcSize * scale;
                Rect dstRect = viewPort
                    .CenterRect(new Rect(scaledSize))
                    .Intersect(viewPort);
                Rect srcRect = new Rect(srcSize)
                    .CenterRect(new Rect(dstRect.Size / scale));

                dstRect = dstRect.Translate(new(slotX, 0));

                context.DrawImage(img, srcRect, dstRect);
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ClearThumbnails();
    }
}
